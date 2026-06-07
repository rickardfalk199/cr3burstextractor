local LrApplication = import 'LrApplication'
local LrTasks = import 'LrTasks'
local LrPathUtils = import 'LrPathUtils'
local LrFileUtils = import 'LrFileUtils'
local LrDialogs = import 'LrDialogs'
local LrProgressScope = import 'LrProgressScope'
local LrFunctionContext = import 'LrFunctionContext'

local function quoteArg(s)
    return '"' .. s:gsub('"', '\\"') .. '"'
end

-- LrTasks.execute on Windows runs through cmd.exe /c, which strips one layer of
-- outer quoting. Wrap the whole command in an extra pair of double quotes so
-- arguments with spaces (paths, redirection targets) survive.
local function wrapForShell(cmd)
    if WIN_ENV then return '"' .. cmd .. '"' end
    return cmd
end

local function escapePattern(s)
    return (s:gsub('([%(%)%.%%%+%-%*%?%[%]%^%$])', '%%%1'))
end

-- Capture the stdout of a single CLI invocation by redirecting to a temp file
-- and reading it back. LrTasks.execute returns only the exit code, so this is
-- the standard idiom for tiny one-line CLI outputs.
local function cliReadString(cliExe, ...)
    local args = { ... }
    local tempPath = LrPathUtils.child(
        LrPathUtils.getStandardFilePath('temp'),
        'cr3be-out-' .. tostring(math.random(1, 1e9)) .. '.txt')

    local pieces = { quoteArg(cliExe) }
    for _, a in ipairs(args) do table.insert(pieces, quoteArg(a)) end
    table.insert(pieces, '>')
    table.insert(pieces, quoteArg(tempPath))
    local cmd = wrapForShell(table.concat(pieces, ' '))

    local exit = LrTasks.execute(cmd)
    local value = ''
    local f = io.open(tempPath, 'r')
    if f then
        value = (f:read('*l') or ''):gsub('^%s+', ''):gsub('%s+$', '')
        f:close()
    end
    pcall(function() LrFileUtils.delete(tempPath) end)
    if exit ~= 0 then return nil end
    return value
end

local function cliRun(cliExe, ...)
    local args = { ... }
    local pieces = { quoteArg(cliExe) }
    for _, a in ipairs(args) do table.insert(pieces, quoteArg(a)) end
    return LrTasks.execute(wrapForShell(table.concat(pieces, ' ')))
end

local function findExtractedFiles(outDir, sourcePath)
    local baseName = LrPathUtils.removeExtension(LrPathUtils.leafName(sourcePath)):lower()
    local pattern = '^' .. escapePattern(baseName) .. '_%d+%.cr3$'
    local sourceLower = sourcePath:lower()
    local results = {}
    for filePath in LrFileUtils.files(outDir) do
        if filePath:lower() ~= sourceLower then
            local leaf = LrPathUtils.leafName(filePath):lower()
            if leaf:match(pattern) then
                table.insert(results, filePath)
            end
        end
    end
    table.sort(results)
    return results
end

LrTasks.startAsyncTask(function()
    LrFunctionContext.callWithContext('Cr3BurstExtractor.ExtractBurstFrames', function(context)
        local catalog = LrApplication.activeCatalog()

        local cliExe = LrPathUtils.child(LrPathUtils.child(_PLUGIN.path, 'bin'),
                                          'Cr3BurstExtractor.Cli.exe')
        if not LrFileUtils.exists(cliExe) then
            LrDialogs.message('CR3 Burst Extractor',
                'CLI executable not found:\n' .. cliExe ..
                '\n\nRun build-plugin.ps1 to populate the bin/ folder.',
                'critical')
            return
        end

        -- Default the picker to whatever the GUI tool (or a previous plugin run)
        -- last used. settings.json is shared between the two via UserSettings.
        local lastFolder = cliReadString(cliExe, '--get-scan-folder')
        if lastFolder == '' then lastFolder = nil end

        local chosen = LrDialogs.runOpenPanel({
            title = 'CR3 Burst Extractor — pick folder',
            prompt = 'Extract',
            canChooseDirectories = true,
            canChooseFiles = false,
            allowsMultipleSelection = false,
            initialDirectory = lastFolder,
        })
        if not chosen or #chosen == 0 then return end
        local scanRoot = chosen[1]

        -- Persist for next time (both this plugin and the GUI tool will pick it up).
        cliRun(cliExe, '--set-scan-folder', scanRoot)

        -- Recursively gather every .CR3 under the picked folder.
        local cr3Paths = {}
        for filePath in LrFileUtils.recursiveFiles(scanRoot) do
            if filePath:lower():sub(-4) == '.cr3' then
                table.insert(cr3Paths, filePath)
            end
        end
        table.sort(cr3Paths)

        if #cr3Paths == 0 then
            LrDialogs.message('CR3 Burst Extractor',
                'No .CR3 files found under:\n' .. scanRoot, 'info')
            return
        end

        local progress = LrProgressScope({
            title = 'Extracting bursts in ' .. LrPathUtils.leafName(scanRoot),
            functionContext = context,
        })
        progress:setCancelable(true)

        local extracted = {}  -- list of { sourcePath, files = { framePaths } }
        local errors = {}
        local skippedNonBurst = 0
        local cancelled = false

        for i, path in ipairs(cr3Paths) do
            if progress:isCanceled() then cancelled = true; break end
            progress:setPortionComplete(i - 1, #cr3Paths)
            progress:setCaption(LrPathUtils.leafName(path))

            local outDir = LrPathUtils.parent(path)
            local exitCode = cliRun(cliExe, path, outDir)

            if exitCode == 0 then
                local files = findExtractedFiles(outDir, path)
                if #files == 0 then
                    table.insert(errors,
                        LrPathUtils.leafName(path) .. ': no output files found')
                else
                    table.insert(extracted, { sourcePath = path, files = files })
                end
            elseif exitCode == 2 then
                skippedNonBurst = skippedNonBurst + 1
            else
                table.insert(errors,
                    LrPathUtils.leafName(path) ..
                        ': CLI exited with code ' .. tostring(exitCode))
            end
        end

        progress:setPortionComplete(1, 1)
        progress:done()

        -- Import all extracted frames in one write transaction and stack each
        -- burst's frames together (no parent to stack under — the source burst
        -- isn't in the catalog — so the first frame becomes the stack top).
        local importedCount = 0
        local stackFailures = 0
        if #extracted > 0 then
            catalog:withWriteAccessDo('Add extracted burst frames', function()
                for _, e in ipairs(extracted) do
                    local stackMembers = {}
                    for _, f in ipairs(e.files) do
                        local existing = catalog:findPhotoByPath(f)
                        local p = existing or catalog:addPhoto(f)
                        if p then
                            table.insert(stackMembers, p)
                            if not existing then
                                importedCount = importedCount + 1
                            end
                        end
                    end

                    if #stackMembers > 1 then
                        local ok = pcall(function()
                            catalog:createPhotoStack(stackMembers, stackMembers[1])
                        end)
                        if not ok then
                            stackFailures = stackFailures + 1
                        end
                    end
                end
            end)
        end

        local lines = {}
        table.insert(lines, 'Folder: ' .. scanRoot)
        table.insert(lines, string.format('CR3 files scanned: %d', #cr3Paths))
        table.insert(lines, string.format('Bursts extracted: %d', #extracted))
        table.insert(lines, string.format('Frames imported: %d', importedCount))
        if skippedNonBurst > 0 then
            table.insert(lines, string.format(
                'Single-frame CR3s skipped: %d', skippedNonBurst))
        end
        if stackFailures > 0 then
            table.insert(lines, string.format(
                'Stacks not created: %d (frames still imported)', stackFailures))
        end
        if cancelled then
            table.insert(lines, '(Cancelled mid-scan.)')
        end
        if #errors > 0 then
            table.insert(lines, '')
            table.insert(lines, 'Errors:')
            for _, e in ipairs(errors) do
                table.insert(lines, '  - ' .. e)
            end
        end

        LrDialogs.message('CR3 Burst Extractor', table.concat(lines, '\n'),
            (#errors > 0) and 'warning' or 'info')
    end)
end)
