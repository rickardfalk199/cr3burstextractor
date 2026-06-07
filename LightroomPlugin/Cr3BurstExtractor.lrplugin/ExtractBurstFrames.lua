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

local function buildCommand(exe, input, outDir)
    local inner = quoteArg(exe) .. ' ' .. quoteArg(input) .. ' ' .. quoteArg(outDir)
    if WIN_ENV then
        -- LrTasks.execute on Windows runs through cmd.exe /c, which strips one layer
        -- of quoting. Wrap the whole command again so paths with spaces survive.
        return '"' .. inner .. '"'
    end
    return inner
end

local function escapePattern(s)
    return (s:gsub('([%(%)%.%%%+%-%*%?%[%]%^%$])', '%%%1'))
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
        local selection = catalog:getTargetPhotos()

        if not selection or #selection == 0 then
            LrDialogs.message('CR3 Burst Extractor', 'No photos selected.', 'info')
            return
        end

        local cr3Photos = {}
        for _, photo in ipairs(selection) do
            local path = photo:getRawMetadata('path')
            if path and path:lower():sub(-4) == '.cr3' then
                table.insert(cr3Photos, { photo = photo, path = path })
            end
        end

        if #cr3Photos == 0 then
            LrDialogs.message('CR3 Burst Extractor',
                'No .CR3 files in the selection.', 'info')
            return
        end

        local cliExe = LrPathUtils.child(LrPathUtils.child(_PLUGIN.path, 'bin'),
                                          'Cr3BurstExtractor.Cli.exe')

        if not LrFileUtils.exists(cliExe) then
            LrDialogs.message('CR3 Burst Extractor',
                'CLI executable not found:\n' .. cliExe ..
                '\n\nRun build-plugin.ps1 to populate the bin/ folder.',
                'critical')
            return
        end

        local progress = LrProgressScope({
            title = 'Extracting burst frames',
            functionContext = context,
        })
        progress:setCancelable(true)

        local extracted = {}
        local errors = {}
        local skippedNonBurst = 0

        for i, entry in ipairs(cr3Photos) do
            if progress:isCanceled() then break end
            progress:setPortionComplete(i - 1, #cr3Photos)
            progress:setCaption(LrPathUtils.leafName(entry.path))

            local outDir = LrPathUtils.parent(entry.path)
            local cmd = buildCommand(cliExe, entry.path, outDir)
            local exitCode = LrTasks.execute(cmd)

            if exitCode == 0 then
                local files = findExtractedFiles(outDir, entry.path)
                if #files == 0 then
                    table.insert(errors,
                        LrPathUtils.leafName(entry.path) .. ': no output files found')
                elseif #files == 1 then
                    skippedNonBurst = skippedNonBurst + 1
                else
                    table.insert(extracted, { parent = entry.photo, files = files })
                end
            else
                table.insert(errors,
                    LrPathUtils.leafName(entry.path) ..
                        ': CLI exited with code ' .. tostring(exitCode))
            end
        end

        progress:setPortionComplete(1, 1)
        progress:done()

        local importedCount = 0
        local stackFailures = 0
        if #extracted > 0 then
            catalog:withWriteAccessDo('Add extracted burst frames', function()
                for _, e in ipairs(extracted) do
                    local stackMembers = { e.parent }
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
                            catalog:createPhotoStack(stackMembers, e.parent)
                        end)
                        if not ok then
                            stackFailures = stackFailures + 1
                        end
                    end
                end
            end)
        end

        local lines = {}
        table.insert(lines, string.format('Bursts processed: %d', #extracted))
        table.insert(lines, string.format('Frames imported: %d', importedCount))
        if skippedNonBurst > 0 then
            table.insert(lines, string.format(
                'Single-frame CR3s (not bursts): %d', skippedNonBurst))
        end
        if stackFailures > 0 then
            table.insert(lines, string.format(
                'Stacks not created: %d (frames still imported)', stackFailures))
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
