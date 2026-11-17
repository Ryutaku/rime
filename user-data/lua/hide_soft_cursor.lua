-- Hide only Rime's U+2038 soft cursor, keeping preedit text visible.
local M = {}

local function hide(ctx)
  if ctx:get_option("soft_cursor") then
    ctx:set_option("soft_cursor", false)
  end
end

function M.init(env)
  local ctx = env.engine.context
  hide(ctx)
  env.soft_cursor_connection = ctx.option_update_notifier:connect(function(context, name)
    if name == "soft_cursor" then hide(context) end
  end)
end

function M.func(key, env)
  hide(env.engine.context)
  return 2 -- Leave all key handling to the existing processors.
end

function M.fini(env)
  if env.soft_cursor_connection then
    env.soft_cursor_connection:disconnect()
  end
end

return M
