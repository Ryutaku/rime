# 小狼毫个人定制存档

日期：2026-11-17。此目录是配置和外挂程序的副本；当前输入法仍使用原 AppData 配置目录。

## 目录

- `user-data/`：可恢复的用户配置、五笔/拼音方案和词库、Lua、手工自造词及外挂程序。
- `install-dir/WeaselWithPhrase.exe`：放在小狼毫安装目录里的联动启动器。
- `startup/`：用户登录启动脚本和开始菜单联动快捷方式的原样副本。
- `machine-state/`：原机器的安装信息、上次方案状态及启动入口记录，供核对。
- `history/backups/`：今天修改前保存的各阶段备份，以及造词文件备份。
- `history/authoring-tools/`：今天的安装、修改脚本和早期验证资料，仅供历史参考。
- `tools/build.ps1`：重新编译现用造词后台和联动启动器。
- `snapshot.json`：原始文件的来源、大小和复制时 SHA-256。
- `SHA256SUMS.txt`：整个存档的文件校验清单。

## 最终配置

1. 默认方案为五笔86＋拼音混输；也保留纯五笔方案文件，但当前方案菜单仅列混输。
2. 数字键和小键盘数字作为原文，不选择候选；隐藏候选数字序号。
3. 字母编码后输入任意英文标点，整段进入原文输入。覆盖全部32个ASCII标点，包括未按Shift的逗号、句号、分号、引号、斜杠、等号及全部Shift符号。
4. 原文段按空格或回车确认，退格编辑，Esc取消。分号、单引号在编码后也按原文处理。中文后接数字或标点时，先空格确认中文。
5. 输入开头的 `;` 开始临时英文；确认时不输出开头的分号。空分号段确认输出中文分号。
6. 左、右Shift不切换中英文；左、右Ctrl切换，存在编码时先确认原始编码（commit_code）。
7. 五笔启用用户词库学习和自动造词；拼音仍为原方案的反查译码器，不具备完整拼音译码器同等的动态调频能力。
8. 中文候选统一转为简体并去重；隐藏预编辑区的 U+2038 `‸` 软光标。
9. `/fh` 常用符号，`/sx` 数学符号，`/jt` 箭头，`/dw` 单位，`/xl` 希腊字母，`/xld` 大写希腊字母，`/fs` 分数。
10. 外观保留现用 cool_breeze 配色、横排、行内编码和隐藏托盘图标等配置，以 weasel.custom.yaml 为准。
11. Windows Terminal、CMD、PowerShell、Git Bash/mintty、Xshell默认英文；应用间不共享全局中英文状态。属于应用默认值，不保证每次切回都强制重置。

## 手工造词外挂

现用文件：

- `user-data/lua/manual_phrase.lua`：Ctrl+Shift+= 提交近期输入请求，不在输入法按键回调里启动进程。
- `user-data/extensions/manual_phrase/ManualPhrase.Resident.cs`：现用后台主源码；编译为 `ManualPhrase.Manager.exe`，复用添加/删除窗口和五笔取码数据。删除界面及文件操作见 ManualPhrase.Delete.cs、ManualPhrase.Store.cs。
- `user-data/extensions/manual_phrase/wubi-single-char.tsv`：从当前五笔词库提取的单字取码缓存。
- `user-data/extensions/manual_phrase/WeaselWithPhrase.cs`：联动启动器源码，对应 `install-dir/WeaselWithPhrase.exe`。
- `启动自造词后台.vbs`：只启动造词后台的备用入口。

Ctrl+Shift+= 后默认取最近两个汉字；↑向前多取一字，↓少取一字；Enter保存，Esc取消。手动编辑词语或编码也可。保存到 custom_phrase.txt，先备份，再异步部署。

关闭窗口只是隐藏后台。常驻程序与窗口在第一次启动时初始化，后续复用；等待快捷键松开后恢复窗口并短暂置前。Windows可能限制自动获取焦点。

旧版 ManualPhrase.exe、ManualPhrase.History.exe 及 ManualPhrase.cs 按原样保存，用于历史回退。现用启动链不调用它们。

## 原机器启动方式

原用户目录：`C:\Users\Administrator\AppData\Roaming\Rime`

原安装目录：`D:\Programs\Rime\weasel-0.17.4`

- 系统 HKLM Run 的 WeaselServer 仍直接指向原 WeaselServer.exe；该键因写权限限制未修改。
- 用户启动目录的 RimeManualPhrase.vbs 登录时启动造词后台。
- 当前用户开始菜单的“小狼毫算法服务”指向 WeaselWithPhrase.exe，联动启动造词后台和小狼毫；系统公共开始菜单的原始入口保留。
- 直接运行原始 WeaselServer.exe 不经过联动入口。

## 恢复到这台机器

1. 备份现有Rime用户目录；关闭造词后台后再替换其可执行文件。
2. 将 `user-data/` 内的内容复制到上面的Rime用户目录，保留原有其他文件。
3. 将 `install-dir/WeaselWithPhrase.exe` 复制到上面的小狼毫安装目录，与 WeaselServer.exe 相邻。
4. 将 `startup/user-login/RimeManualPhrase.vbs` 放回当前用户的“启动”目录；将 `startup/user-start-menu/小狼毫算法服务.lnk` 放回当前用户开始菜单的“小狼毫输入法”目录。
5. 从开始菜单启动联动入口，然后重新部署小狼毫，验证输入。

不同电脑、用户名或安装路径：先安装小狼毫和其内置Lua/OpenCC支持，再调整VBS、快捷方式中的绝对路径。联动启动器读取当前用户注册表的RimeUserDir；无此值时使用当前用户AppData\Roaming\Rime。联动启动器应放在目标WeaselServer.exe旁边。

不要直接把 machine-state/installation.yaml 或 user.yaml 覆盖到其他机器，它们是原机器状态。不要从存档目录直接运行造词后台作为正式安装：它以自身目录定位custom_phrase.txt，而部署程序仍针对注册的用户目录。

## 备份范围和验证状态

已保存 custom_phrase.txt 中的现有自造词，以及全部现用Lua、配置、词库和外挂源码/可执行文件。运行期间的request文件、错误日志、可重新生成的build目录没有打包。

正在使用的 wubi86.userdb、stroke.userdb 数据库未直接复制，避免活动数据库快照不一致。需要保留动态调频学习结果时，请另用小狼毫用户词典管理导出快照，或退出输入法后备份数据库。

本次存档仅复制文件、记录启动状态并校验文件哈希。没有重启、部署或测试输入法。history中的验证结果属于早期版本，不能作为最终配置的验证结果。

## 常用词补充（2026-11-17）

新增 wubi86_extra.dict.yaml（269条标准五笔）和 pinyin_simp_extra.dict.yaml（221条拼音），分别由原基础词库的import_tables导入。恢复时要同时复制修改后的基础词库和这两份扩展词库。完整编码及核对说明见reports/补词清单-2026-11-17.tsv和reports/补词说明-2026-11-17.md。仅静态检查，需重新部署后验证。

## 候选方向键（2026-11-17）

混输和纯五笔均补充显式方向键绑定：左键上一候选、右键下一候选，空格确认。横排、竖排布局均设置，Tab及Shift+Tab保留。需重新部署后验证。


## 快捷删除自造词（2026-11-17）

现用后台更新为 `ManualPhrase.Manager.exe`。首次升级时先关闭造词窗口，双击用户目录 extensions/manual_phrase/重启造词后台.vbs，再重新部署。脚本仅替换运行中的造词后台；该次配置任务未实际运行此脚本。

- Ctrl+Shift+=：添加自造词。
- Ctrl+Shift+-：打开手工自造词删除窗口，默认显示全部，可按词语或编码搜索。
- 输入词语或编码筛选，↓进入列表，↑↓选择，Delete或Enter删除；Esc关闭。
- 删除和撤销都在修改前备份；异步部署后继续操作；“撤销上次”恢复最近一次删除的词语＋编码，保留期间新增的其他词。
- 此窗口管理custom_phrase.txt，基础/扩展词库和自动学词仍可能提供同词候选。
- 原ManualPhrase.Resident.exe保留为旧版回退。登录脚本和联动启动器已改用新程序。
- tests/ManualPhrase.Store.Tests.cs：13项文件删除和撤销检查，使用独立临时目录，无输入法或界面操作。

重新编译执行tools/build.ps1，现用后台包含Resident、Delete、Store三份源码。原README前文的后台运行机制保持一致。


## 删除窗口从其他应用唤出时的焦点（2026-11-17）

Ctrl+Shift+-改由后台RegisterHotKey/WM_HOTKEY接收，在快捷键处理内立即取得前台激活。删除窗口移除了750毫秒临时置顶逻辑，并在实际激活后聚焦搜索框；同一快捷键产生的重复Lua请求在一秒内忽略。全局快捷键入口默认显示全部手工词组，支持编码或词语搜索，中英文状态下均可使用。输入法Lua入口保留作为注册失败时的后备。

此方案不连接其他应用输入队列、不模拟按键。Windows快捷键激活权限依据：https://devblogs.microsoft.com/oldnewthing/20090226-00/?p=19013 。仅编译检查和后台启动检查，实际跨应用输入场景仍由使用者验证。

## 候选删除和自动组句（2026-11-17）

混输和纯五笔均显式绑定Shift+Delete和Control+Delete到delete_candidate，并关闭主translator/enable_sentence，避免继续临时拼出不存在于固定词库的组合。已有用户词库调频保留。详细依据、限制及待验证项见reports/删除候选与自动组句-2026-11-17.md。需重新部署后验证。
