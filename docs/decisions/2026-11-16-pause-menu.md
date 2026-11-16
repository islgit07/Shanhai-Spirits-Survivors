# 2026-11-16 局内暂停菜单（PAUSE-001）决策留档

## 需求
游戏内按 ESC 弹出 UI，含「继续 / 设置 / 退出」。用户称其为「小插曲」，版本保持 `0.0005`。

## 选择
- **模式**：`godot-ui` 的 pause menu overlay。独立 `scenes/ui/PauseMenu.tscn`，根节点 `CanvasLayer`（`process_mode = Always`，layer 10）→ 全屏半透遮罩 `ColorRect` → `CenterContainer` → `PanelContainer` → `VBoxContainer`（标题 + 三按钮 + 提示）。`Main.tscn` 以 PackedScene 实例挂载，默认隐藏。
- **输入**：`input-handling` 的 `_UnhandledInput` + 原生 `InputEventKey.Keycode == Key.Escape`，按下沿触发、忽略 Echo，处理后 `SetInputAsHandled()`。ESC 语义：未开→打开；设置页→退回菜单；菜单→继续。
- **状态**：`GetTree().Paused = true/false`；菜单节点 `ProcessModeEnum.Always` 保证暂停时仍可点/可导航。打开时 `grab_focus()` 落在「继续」，三按钮焦点成环。
- **退出**：`ChangeSceneToFile("res://scenes/StartPage.tscn")`，回开始页而非直接关窗。
- **设置**：只有入口和占位页（「设置项后续补充。」+ 返回），不塞音量/分辨率等未点名功能。

## 否决
1. 新增 `pause` Input Map action —— `project.godot` 有用户未提交改动，本轮不碰；手柄/Start 键映射留到统一做手柄支持时再接。
2. `_input` 抢先截 ESC —— 会抢在 UI 前消费事件，破坏 `ui_cancel` 导航。
3. 纯代码拼 UI —— 项目约定 UI 走 `.tscn`，版式以后可直接调。
4. `GetTree().Quit()` —— 「退出」先按游戏内暂停菜单惯例回主菜单。
5. 设置页做真实配置项 —— 用户没点名，不越界。

## 边界
不加战斗/存档/局内成长逻辑；不改版本号、不打 tag、不 push；README 用户自管；`project.godot` 与 `StartPage.tscn` 的用户改动保持原样。
