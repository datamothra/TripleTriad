# 卡农 30 秒试玩（简单版）

打开 Assets/Scenes/Triad.unity，点击 Unity Play，然后按 F1 开始。四秒准备后播放音乐。

| 玩家 | 移动 | 金色和弦交互 | 灰色和弦交互 |
|---|---|---|---|
| P1（蓝） | WASD | 空格 | 左 Shift |
| P2（橙） | 方向键 | 主键盘回车 | 右 Shift |
| P3（紫） | 小键盘 5 上、1 左、2 下、3 右 | 小键盘 0 | 小键盘 . |

- 三名玩家自动生成。手柄按连接顺序也可控制 P1/P2/P3：左摇杆或方向键移动，Cross/A 按金色和弦，Square/X（button West）按灰色和弦，Start 重开。
- 金色圆环到达中央红色判定圈时，三人分别位于和弦的三个音区，并按各自的交互键。
- 灰色弧条同样向中央收缩。到达红圈时，三人需各自站在和弦的一个音区，并按灰色和弦交互键（手柄 Square/X）；同一区挤三人不算成功。玩家在音区内的径向位置不影响判定。
- 同一和弦里的三个音可以由任意玩家分担。
- Perfect 为 ±150ms，Good 为 ±300ms。一个按键只属于一个金色目标。
- F1 / R 重新开始；Esc 暂停/继续。窗口失去焦点自动暂停。
- 失误不会中断音乐。30 秒结束显示分数和失误数。
- 金色 Perfect 150 分、Good 100 分；灰色成功 50 分。

## 音频与谱面

使用用户提供的 clavier-music-pachelbelx27s-canon-canon-in-d-307319.mp3 前 30 秒，转为 WAV，最后 0.5 秒淡出，原文件没有修改。

音频：Assets/Resources/Canon30.wav。
谱面：Assets/Resources/CanonChart.json。

简单版从原录音标记中每隔一个和弦选取一次操作，共 9 个金色和弦，间隔约 3.4 秒。后半段只保留 4 组灰色弧条，沿用刚弹完的和弦位置，站住并按 West 键即可；灰条后距离下一次换位目标仍有约 2.5 秒。提示提前 3 秒出现。音乐保持原速，所有时间点仍对应原录音。

背景保留完整钢琴录音；金色命中叠加较轻的合成音。当前不是可以把原录音中的某个声部静音的分轨伴奏。

音乐用 PlayScheduled 开始，谱面与按键共用 DSP 音频时钟。CanonRhythm 的 timingOffsetMs 正数代表将判定整体延后，可以在运行时微调设备延迟。

## 实现位置

- CanonRhythm.cs：音乐、三人键盘/手柄输入、金色和灰色目标、判定、暂停与结算。中文注释标注主要逻辑。
- GameManager.cs：默认进入卡农音乐模式；关闭 Inspector 中 Canon Music Mode 可恢复原来的选曲与手柄加入模式。
- PlayerVoice.cs：音乐模式把更新交给 CanonRhythm，避免同一帧移动两次。
- Tools/build_canon_chart.py：生成当前逐点谱面。
- Tools/analyze_canon.py：本地音频分析与截取记录。
- Triad > Validate Canon (Play Mode)：运行约 35 秒的回归检查；需先打开已保存的 Triad 场景。结果写入 Tools/canon-check.txt。

普通键盘同时按很多键时可能受硬件键位冲突限制；游戏支持三人同时输入，但具体键盘的多键识别能力仍需三人实机试玩确认。
