-- Ctrl+Shift+= adds a phrase; Ctrl+Shift+- opens the manual phrase deletion list.
-- No process creation, shell command, window activation, or waiting in the IME.
local M = {}
local ACCEPTED, NOOP = 1, 2

function M.init(env)
  local directory = rime_api.get_user_data_dir() .. '/extensions/manual_phrase/'
  env.request = directory .. 'request.ready'
  env.pending = directory .. 'request.pending'
  env.hotkey_down = false
end

local function recent_input(ctx)
  local ok, text = pcall(function()
    local parts = {}
    for _, record in ipairs(ctx.commit_history:to_table()) do
      if type(record.text) == 'string' then parts[#parts + 1] = record.text end
    end
    if ctx:is_composing() then parts[#parts + 1] = ctx:get_commit_text() end
    return table.concat(parts)
  end)
  if not ok or type(text) ~= 'string' then return '' end
  -- Keep the handoff small; discard partial UTF-8 at the new left edge.
  if #text > 16384 then
    text = text:sub(-16384):gsub('^[\128-\191]+', '')
  end
  return text
end

function M.func(key, env)
  local code = key.keycode
  local shortcut_key = code == 0x2b or code == 0x3d or code == 0x5f or code == 0x2d
  if key:release() then
    if shortcut_key then env.hotkey_down = false end
    return NOOP
  end
  if not key:ctrl() or key:alt() or key:super() then return NOOP end
  local add = code == 0x2b or (code == 0x3d and key:shift())
  local remove = code == 0x5f or (code == 0x2d and key:shift())
  if not add and not remove then return NOOP end
  if env.hotkey_down then return ACCEPTED end
  env.hotkey_down = true
  -- Publish only a complete request. The helper watches the final filename.
  pcall(function()
    local request = io.open(env.pending, 'wb')
    if not request then return end
    local text
    if remove then
      local ok, selected = pcall(function()
        local candidate = env.engine.context:get_selected_candidate()
        return candidate and candidate.text or env.engine.context.input
      end)
      text = ok and type(selected) == 'string' and selected or ''
    else
      text = recent_input(env.engine.context)
    end
    local mode = remove and 'delete' or 'add'
    local written = request:write('RIME_PHRASE_V1\t' .. mode .. '\n' .. text)
    local closed = request:close()
    if written and closed then
      os.remove(env.request)
      os.rename(env.pending, env.request)
    end
  end)
  return ACCEPTED
end

return M
