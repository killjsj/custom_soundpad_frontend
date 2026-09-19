# CustomSoundpad Frontend（Godot 4.7 + C#）
> 更正: C++ Out!  

播放音频 -> 抓帧 -> 写入 APO 共享内存（`Global\InjectAudioPcmRingBuffer`）-> 由 APO 注入麦克风。
同时提供 ApoWriter 的 **C#** 与  两套实现，可随时切换对比。

## 目录结构

| 文件 | 职责 |
| --- | --- |
| `SoundpadMain.cs` | 核心：可配置项、字段、`_Ready`/`_Process` 装配、状态栏 |
| `SoundpadMain.Events.cs` | 节点缓存 + 事件注册（按播放/设备/热键/歌单/悬浮窗分组） |
| `SoundpadMain.Audio.cs` | 采集总线、播放/暂停/停止、进度与拖动、监听开关、内置测试音 |
| `SoundpadMain.Tracks.cs` | 音轨导入/拖放、行管理、选择、循环、单曲设置对话框 |
| `SoundpadMain.Devices.cs` | 设备表、注册/取消绑定/注销队列 |
| `SoundpadMain.Hotkeys.cs` | 全局热键绑定与设置对话框、录制 |
| `SoundpadMain.Playlist.cs` | 歌单保存/读取 |
| `SoundpadMain.Writers.cs` | C#/C++ ApoWriter 桥接与切换、0x05 重试 |
| `SoundpadMain.Overlay.cs` | 透明无边框悬浮窗：创建/定位/开关/内容刷新 |
| `OverlayHud.cs` | 悬浮窗 HUD 内容：状态、歌曲列表、热键、视效、进度条 |
| `GlobalHotkeys.cs` | 全局热键：低级键盘/鼠标钩子（失焦可用）+ 手柄轮询；录制、下传（拦截）开关 |
| `AudioVisualizer.cs` | 频谱可视化：条形图 / 圆圈（可旋转），数据来自总线上的 SpectrumAnalyzer |
| `apo/ApoWriter.cs` | C# 版 ApoWriter（Node）：共享内存 + 注册/绑定/取消绑定/注销 |
| `apo/ApoInstaller.cs` | C# 版注册逻辑：Core Audio API 枚举设备、签名、regsvr32、注册表绑定/解绑/注销 |
| `apo/ApoBindingGuard.cs` | 注册表守护：EFX 绑定/处理模式/设备增删监控 |
| `apo/PcmRingBuffer.cs` | 与 C++ 端一致的共享内存布局与读写 |
| `../src/ApoWriter.*` | C++ GDExtension 单例（`Engine.GetSingleton("ApoWriter")`） |
| `../src/ApoInstaller.*` | C++ 版注册逻辑（与 C# 对等） |
| `../src/ApoBindingGuard.*` | C++ 版守护 |
| `control.tscn` | 场景：全部 UI 节点（脚本在根节点） |

## 构建 / 运行

```powershell
# C#
dotnet build "apoFrontend.csproj"
```

- 注册/绑定/注销 APO 需要**管理员权限**；环形缓冲（Global 映射）同样需要。
- 非管理员时仍可播放音频与查看可视化，状态栏会提示 0x05 并在录音激活后自动重试。

## 常用扩展点

1. **新增全局热键动作**：在 `SoundpadMain` 里实现一个方法（如 `PlayNext`），
   在 `_Ready` 中调用 `_hotkeys.Register(combo, passthrough, action)`；
   对话框里加一行即可（`BuildHotkeyDialog` 的 `labels` 数组 + `_hotkeyButtons`）。
2. **新增单曲热键/选项**：扩展 `Track` 字段 -> `OpenTrackSettings` 对话框 -> `ApplyTrackDialog` 应用。
   热键注册统一走 `RegisterTrackHotkey`。
3. **新增可视化模式**：`AudioVisualizer.cs` 的 `Mode` + `_Draw` 分支即可，
   下拉框在 `_Ready` 里 `_visualizerMode.AddItem(...)`。
4. **新增音频格式**：`LoadAudioFile` 的扩展名分支 + `ApplyLoop` 的循环设置分支。
5. **新增 ApoWriter 实现**：实现同样方法名（`try_open_ring_buffer` / `write_pcm` / ...），
   在 `UseCpp`/`WriterLabel`/`EnsureWriterOpen`/`WriteFrames` 处接入。
6. **可配置项**：`SoundpadMain` 的 `[Export]`（窗口起始/最小/最大尺寸、0x05 重试间隔、
   悬浮窗开关/尺寸/位置/视效模式）与 `AudioVisualizer` 的 `[Export]`（圆圈大小/转速）均可在检查器调整。
   悬浮窗默认锁定（鼠标穿透、不可聚焦、置顶）；取消「锁定悬浮窗」后可直接拖动。

## 歌单 JSON 格式

```json
{
  "tracks": [
    { "path": "D:/music/a.wav", "name": "a.wav", "hotkey": "F7",
      "passthrough": false, "loop": true }
  ],
  "hotkeys": {
    "play": { "combo": "Ctrl+Alt+P", "passthrough": true },
    "prev": { "combo": "鼠标中键", "passthrough": false },
    "next": { "combo": "手柄A", "passthrough": false }
  }
}
```

- 键盘：`Ctrl+Alt+A`、`F7`、`Esc`（任意单键即可，不强制带修饰键）。
- 鼠标：`鼠标左键` / `鼠标右键` / `鼠标中键` / `鼠标侧键1` / `鼠标侧键2` / `滚轮上` / `滚轮下`。
- 手柄：`手柄A`、`手柄B`、`手柄X`、`手柄Y`、`手柄LB`、`手柄RB`、`手柄Back`、`手柄Start`、
  `手柄L3`、`手柄R3`、`手柄十字上/下/左/右`、`手柄LT`、`手柄RT`。
- `passthrough`：true = 热键同时下传给其他应用；false = 拦截（仅本应用响应）。
