-- Digits and all printable ASCII punctuation after a letter code are literal input.
-- Alt+semicolon starts one temporary ASCII token.
-- Confirm a literal token with Space or Enter; Escape cancels it.
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

function M.processor(key, env)
  if key:release() or key:ctrl() or key:super() then return NOOP end
  local ctx = env.engine.context
  if ctx:get_option('ascii_mode') then return NOOP end
  local code, input = key.keycode, ctx.input
  local char = printable(key)
  if key:alt() then
    if input == '' and not key:shift() and (char == ';' or code == 0x2f
        or (char == '`' and env.engine.schema.config:get_string('reverse_lookup/prefix') == char)) then
      ctx:push_input(char)
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
      return ACCEPTED
    end
    if code == BACKSPACE then
      ctx:pop_input(1)
      return ACCEPTED
    end
    if char then
      ctx:push_input(char)
      return ACCEPTED
    end
    return NOOP
  end
  local digit = char and char:match('^[0-9]$')
  local symbol = literal_symbol(char)
  if digit or symbol then
    if input:match('^[A-Za-z]') then
      ctx:push_input(char)
      return ACCEPTED
    end
    if input == '' then
      local text = digit or initial_punctuation(char, key, env)
      if text then
        env.engine:commit_text(text)
        return ACCEPTED
      end
    end
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
