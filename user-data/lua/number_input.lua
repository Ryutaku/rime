-- Digits and all printable ASCII punctuation after a letter code are literal input.
-- Semicolon starts one temporary ASCII token; Alt+semicolon does the same.
-- Confirm a literal token with Space or Enter; Escape cancels it.
-- A punctuation key typed right after a committed digit or letter is read
-- literally, so 3 . 1 4 gives 3.14 and 10 : 30 gives 10:30.
-- Programs listed in number_input/ascii_lock_apps are kept in ASCII mode.
-- After the phrase manager redeploys, the first key in that program restores the
-- Chinese/English state that was in use before saving.
local M = {}
local ACCEPTED, NOOP = 1, 2
local RETURN, KP_ENTER, BACKSPACE, SPACE = 0xff0d, 0xff8d, 0xff08, 0x20

-- US-keyboard Shift symbol pairs. Some frontends report the shifted keysym,
-- others can retain the base key together with the Shift modifier.
local shifted = {
  [0x60] = '~',
  [0x31] = '!', [0x32] = '@', [0x33] = '#', [0x34] = '$',
  [0x35] = '%', [0x36] = '^', [0x37] = '&', [0x38] = '*',
  [0x39] = '(', [0x30] = ')',
  [0x2d] = '_', [0x3d] = '+',
  [0x5b] = '{', [0x5d] = '}', [0x5c] = '|',
  [0x3b] = ':', [0x27] = '"',
  [0x2c] = '<', [0x2e] = '>', [0x2f] = '?',
}

-- Last character committed by this processor, "" after any other key. A
-- punctuation key after a digit or a letter is read literally, while 汉字 before
-- it still gives 中文标点.
local last_char = ''

local function printable(key)
  local code = key.keycode
  -- Shift normalization must precede digit handling: Shift+1 means !, not 1.
  if key:shift() and shifted[code] then return shifted[code] end
  if code >= 0xffb0 and code <= 0xffb9 then return tostring(code - 0xffb0) end
  if code == 0xffad then return '-' end -- Keypad subtract.
  if code == 0xffab then return '+' end -- Keypad add.
  if code == 0xffaa then return '*' end -- Keypad multiply.
  if code == 0xffaf then return '/' end -- Keypad divide.
  if code == 0xffae then return '.' end -- Keypad decimal.
  if code == 0xffac then return ',' end -- Keypad separator.
  if code == 0xffbd then return '=' end -- Keypad equal.
  if code > SPACE and code < 0x7f then return string.char(code) end
end

local function literal_symbol(char)
  if not char then return false end
  local code = char:byte()
  -- All 32 ASCII punctuation characters, with and without Shift.
  return (code >= 0x21 and code <= 0x2f)
      or (code >= 0x3a and code <= 0x40)
      or (code >= 0x5b and code <= 0x60)
      or (code >= 0x7b and code <= 0x7e)
end

local function raw_input(input)
  if not input:match('^[A-Za-z]') then return false end
  if input:find('%d') then return true end
  -- Allow existing spelling delimiters before the first literal symbol too.
  for char in input:gmatch('.') do
    if literal_symbol(char) then return true end
  end
  return false
end

local function initial_punctuation(char, key, env)
  local ctx = env.engine.context
  if key.keycode > 0x7e or ctx:get_option('ascii_punct') then return char end
  local shape = ctx:get_option('full_shape') and 'full_shape' or 'half_shape'
  local mapping = env.engine.schema.config:get_map('punctuator/' .. shape)
  local definition = mapping and mapping:get(char)
  if not definition then return char end
  local choices = definition:get_list()
  if choices then
    local first = choices:get_value_at(0)
    return first and first.value or char
  end
  local settings = definition:get_map()
  if settings and settings:has_key('pair') then return nil end
  local value = settings and settings:get_value('commit') or definition:get_value()
  return value and value.value or char
end

-- Reads a string from the schema config; nil when the key is missing or the
-- value is not a string, so a config problem never breaks a key press.
local function config_string(env, key_name)
  local config = env.engine.schema.config
  if not config then return nil end
  local value = config:get_string(key_name)
  return type(value) == 'string' and value or nil
end

-- A punctuation key typed right after a digit or a letter is read literally:
-- 3.14, 192.168.1.1:8080, 10:30, name: value. Which keys count is listed in
-- number_input/ascii_punct_after_digit and .../ascii_punct_after_letter; an
-- empty value turns that case off.
local function literal_punct(char, key, env)
  if not (char and last_char ~= '') then return false end
  local key_name
  if last_char:match('^[0-9]$') then
    key_name = 'number_input/ascii_punct_after_digit'
  elseif last_char:match('^[A-Za-z]$') then
    key_name = 'number_input/ascii_punct_after_letter'
  else
    return false
  end
  local chars = config_string(env, key_name)
  if not (chars and chars:find(char, 1, true)) then return false end
  local code = key.keycode
  -- Keypad decimal reports its own keysym; full shape keeps full-width marks.
  if code > 0x7e and code ~= 0xffae then return false end
  return not env.engine.context:get_option('full_shape')
end

-- Weasel applies app_options once, when the app's input session is created, and
-- Xshell sets the IME state back to Chinese by itself. Every key checks the
-- programs listed in number_input/ascii_lock_apps and switches them back to
-- ASCII; pending codes are committed as raw text so no keystroke is lost.
-- DIAG(临时): 定位「Xshell 里没锁成英文」。日志写到
-- %TEMP%\rime-lock-diag.log，最多 300 行；定位完删掉这一函数和下面的调用。
local diag_path = 'C:/Users/Administrator/AppData/Local/Temp/rime-lock-diag.log'
local diag_lines = 0
local function diag(msg)
  if diag_lines >= 300 then return end
  diag_lines = diag_lines + 1
  local ok = pcall(function()
    local file = io.open(diag_path, 'a')
    if file then file:write(msg, '\n'); file:close() end
  end)
  if not ok then print('[lock_ascii] ' .. msg) end
end

local function lock_ascii(env)
  local apps = config_string(env, 'number_input/ascii_lock_apps')
  local ctx = env.engine.context
  local app = ctx.get_property and ctx:get_property('client_app')
  diag(string.format('has_get_property=%s apps=%s app=%s type=%s ascii=%s',
    tostring(ctx.get_property ~= nil), tostring(apps), tostring(app),
    type(app), tostring(ctx:get_option('ascii_mode'))))
  if not apps or apps == '' then return end
  if not ctx.get_property then return end
  if type(app) ~= 'string' or app == '' then return end
  for entry in apps:gmatch('[^;]+') do
    if entry:match('^%s*(.-)%s*$'):lower() == app then
      if not ctx:get_option('ascii_mode') then
        if ctx.input ~= '' then env.engine:commit_text(ctx.input) end
        ctx:clear()
        if ctx.set_option then ctx:set_option('ascii_mode', true) end
        diag('locked ascii_mode=true for ' .. app)
      end
      return
    end
  end
end

-- True for the programs listed in number_input/ascii_lock_apps, which stay in
-- English on purpose.
local function is_locked(env, app)
  local apps = config_string(env, 'number_input/ascii_lock_apps')
  if not apps or apps == '' then return false end
  for entry in apps:gmatch('[^;]+') do
    if entry:match('^%s*(.-)%s*$'):lower() == app then return true end
  end
  return false
end

-- Saving or deleting a phrase redeploys, and the rebuilt session re-applies the
-- app_options that put terminals in English. The phrase manager writes one
-- request naming the program and the Chinese/English state it found there; the
-- first key in that program's new session puts the state back. Requests older
-- than two minutes, and requests for a locked program, are dropped.
local function restore_state(env)
  if env.restore_done then return end
  env.restore_done = true
  pcall(function()
    local ctx = env.engine.context
    if not ctx.get_property then return end
    local app = ctx:get_property('client_app')
    if type(app) ~= 'string' or app == '' then return end
    app = app:lower()
    local path = rime_api.get_user_data_dir()
        .. '/extensions/manual_phrase/restore-ascii.ready'
    local file = io.open(path, 'rb')
    if not file then return end
    local body = file:read('*a')
    file:close()
    local fields = {}
    for field in (body .. '\t'):gmatch('([^\t]*)\t') do fields[#fields + 1] = field end
    if fields[1] ~= 'RIME_RESTORE_V1' then os.remove(path) return end
    local stamp = tonumber(fields[4])
    if not stamp or os.time() - stamp > 120 then os.remove(path) return end
    if type(fields[2]) ~= 'string' then os.remove(path) return end
    -- Keep a request that belongs to another program for its own session.
    if fields[2]:lower() ~= app then return end
    os.remove(path)
    if is_locked(env, app) then return end
    local target = fields[3] == '1'
    if ctx.set_option and ctx:get_option('ascii_mode') ~= target then
      ctx:set_option('ascii_mode', target)
    end
  end)
end

function M.init(env)
  env.restore_done = false
end

function M.processor(key, env)
  lock_ascii(env)
  restore_state(env)
  if key:release() or key:ctrl() or key:super() then return NOOP end
  local ctx = env.engine.context
  if ctx:get_option('ascii_mode') then
    last_char = ''
    return NOOP
  end
  local code, input = key.keycode, ctx.input
  local char = printable(key)
  if key:alt() then
    if input == '' and not key:shift() and (char == ';' or code == 0x2f
        or (char == '`' and env.engine.schema.config:get_string('reverse_lookup/prefix') == char)) then
      ctx:push_input(char)
      last_char = ''
      return ACCEPTED
    end
    return NOOP
  end
  local temporary_ascii = input:sub(1, 1) == ';'
  if temporary_ascii or raw_input(input) then
    if code == SPACE or code == RETURN or code == KP_ENTER then
      local text = temporary_ascii and input:sub(2) or input
      if temporary_ascii and text == '' then text = '；' end
      env.engine:commit_text(text)
      ctx:clear()
      last_char = text:sub(-1)
      return ACCEPTED
    end
    if code == BACKSPACE then
      ctx:pop_input(1)
      last_char = ctx.input:sub(-1)
      return ACCEPTED
    end
    if char then
      ctx:push_input(char)
      last_char = char
      return ACCEPTED
    end
    -- Arrow keys, Tab or Escape in a literal segment: forget the tail character.
    last_char = ''
    return NOOP
  end
  local digit = char and char:match('^[0-9]$')
  local symbol = literal_symbol(char)
  if digit or symbol then
    if input:match('^[A-Za-z]') then
      ctx:push_input(char)
      last_char = char
      return ACCEPTED
    end
    -- Plain ";" starts one temporary ASCII token. ";" alone, confirmed with
    -- Space or Enter, still commits the full-width 分号.
    if char == ';' and input == '' then
      ctx:push_input(';')
      return ACCEPTED
    end
    if input == '' then
      local text = digit
      if not text and literal_punct(char, key, env) then text = char end
      if not text then text = initial_punctuation(char, key, env) end
      if text then
        env.engine:commit_text(text)
        last_char = text:sub(-1)
        return ACCEPTED
      end
    end
  end
  -- Any other real key (letters, Space, Escape) starts a new context. A modifier
  -- press must not: ":" is typed as Shift then ";", and the remembered digit has
  -- to survive that Shift, so keys held with a modifier keep the state.
  if not (key:shift() or key:alt() or key:ctrl() or key:super()) then
    last_char = ''
  end
  return NOOP
end

function M.translator(input, seg, env)
  local text
  if input:sub(1, 1) == ';' then
    text = input:sub(2)
    if text == '' then return end
  elseif seg:has_tag('raw_numbers') or seg:has_tag('raw_symbols') then
    text = input
  else
    return
  end
  local candidate = Candidate('literal_ascii', seg.start, seg._end, text, '')
  candidate.preedit = text
  yield(candidate)
end

return M
