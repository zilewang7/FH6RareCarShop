FH6 奇珍商店工具
版本 2.2.1  |  Windows x64  |  支持 Xbox 应用版与 Steam 版

本版支持城市生活系列新增的东京北改装车点位（1 个展位、15 辆），默认选择
1997 Nissan Skyline GT-R V-Spec（R33）；1999 Toyota Altezza RS200 Z EDITION 与 R33
是本系列新增的难寻车辆，列表中以紫色“难寻”标签标出。Xbox 3.483.970.0 与
Steam 6.483.970.0 均已在游戏内完成扫描、资格诊断、切换与完整重购事务实测，
尚未在新构建上做真实购买闭环。

这两个构建的完整刷新都不再按工具指定的临时车池选车（Xbox 固定选中 R33，Steam
停在第一中转车上）。工具会在刷新落到目标车或第一中转车时，用 setter 再切一次
同池中转车，然后切回目标车。

支持的活动
==========
东京北改装车（城市生活）：首次开放 2026-11-05 22:30 至 2026-12-03 22:30（UTC+8），
1 个展位，15 辆。位于东京城北 Kado mart 旁的汽车修理店门口，默认选择 R33。
Mazda Furai 不在该车池中（本系列的 Furai 加入的是 Spec Racing）。

7-Eleven 联动：首次开放 2026-10-08 22:30 至 2026-11-05 22:30（UTC+8），1 个展位，
14 辆。位于南岸区 7-Eleven 店后，默认选择 Mazda Furai。
AMG CLK GTR 为抽奖限定；Furai、MR2 SC 为嘉年华游戏列表回归稀有车。

英国奇珍：首次开放 2026-09-10 22:30 至 2026-10-08 22:30（UTC+8），2 个展位，
每槽 11 辆，共 22 辆。

意大利奇珍：首次开放 2026-07-16 22:30 至 2026-08-13 22:30（UTC+8），3 个展位，
每槽 14 辆，共 42 辆。

工具默认选择首次开放时间最晚的活动，也可在顶栏切换。首次时间用于标识活动版本，
不代表活动不会复刻；实际开放状态以游戏内为准。只有游戏内槽数、完整车池与所选活动
一致时，工具才会继续写入。

用途
====
选择当前奇珍商店各展位展示的车辆，并为已经购买后购买入口消失（可能同时没有车辆
模型）的车辆尝试恢复重购交互。

与随机刷新的关系
================
工具不修改存档、车库、货币或网络请求。普通切换与重购刷新调用游戏自身的展位逻辑，
候选车辆仍来自所选活动原有车池。从日志能观察到的结果看，游戏只是正好“随机”到
所选车辆；最终效果与完全退出游戏、重新进入并等待随机命中相同，只是节省了时间。

截至已验证版本，未观察到 FH6 为此流程部署独立反作弊拦截。这不代表官方授权、永久
兼容或零风险。

使用步骤
========
1. 启动 Forza Horizon 6，进入当前开放的奇珍商店，等待全部车辆模型出现。
2. 以管理员身份运行 FH6RareCarShop.exe。
3. 在顶栏选择与游戏内一致的活动；默认是最新活动。
4. 可按原名、中文译名、品牌、年份、库存 ID 或车型 ID 搜索。
5. 为各展位选择车辆，使用“应用此展位”或“应用全部展位”。
6. 普通切换完成后，回到游戏查看车辆。

恢复重购
========
仅在目标车已经购买、购买入口消失时使用。若车辆模型也同时消失，可直接执行恢复，
无需先手动“应用此展位”：

1. 在对应展位选择刚买过的目标车，点击“恢复重购”。
2. 确认后立即切回游戏，停留在当前奇珍商店场地。
3. 工具只检查目标展位；若交互已经卸载，会先用同池临时车辆重新加载。
4. 随后工具恢复资格、调用游戏完整刷新，再切回目标车。
5. 等待工具显示完成后，在场地内确认购买入口。

完整刷新依赖游戏线程；等待期间不要强制关闭工具。若退出游戏，所有进程内临时状态
会随游戏进程一起清除。

限定、推荐与难寻
================
英国抽奖限定：
2019 Aston Martin DBS Superleggera
1961 Jaguar E-type

意大利抽奖限定：
2012 Ferrari 599XX Evolution
2011 Lamborghini Sesto Elemento
1984 Ferrari 288 GTO
1999 Lamborghini Diablo GTR
2012 Lamborghini Aventador LP700-4
2019 Ferrari F8 Tributo

特别推荐：599XX Evolution、Sesto Elemento。

难寻（列表中显示紫色“难寻”标签，也可搜索“难寻”）：东京北改装车的
1997 Nissan Skyline GT-R V-Spec（R33）、1999 Toyota Altezza RS200 Z EDITION。

兼容与安全检查
==============
- 工具不修改存档文件，不抓取网络流量。
- 普通切换调用游戏自己的展位 setter。
- 所选活动、槽数、完整车池、车型映射、函数特征和资格 getter 必须全部通过校验。
- 中转车只能来自所选活动的同一展位池。
- 空展位或已卸载的其他展位不会阻断当前目标展位的重购操作。
- 临时车池和资格写入采用事务式回滚；游戏线程状态未知时不会强制回滚。
- 游戏更新后若代码特征不再唯一匹配，工具会停用对应写入功能。
- “只读扫描”不会修改游戏内存，可先在诊断页确认兼容状态。

已知验证范围
============
- 东京北改装车已在 Xbox 3.483.970.0 与 Steam 6.483.970.0 完成单展位扫描、资格诊断、
  R33 / Altezza 切换与完整重购事务（含刷新未按临时车池选车时的二次中转）；尚未做
  真实购买闭环。
- 7-Eleven 联动已在 Xbox 3.461.691.0 完成单展位扫描、资格诊断、切换与完整重购事务，
  并以 MR2 SC 完成实机购买闭环：购买后车辆模型与入口一同消失，恢复后重新出现并可购买。
- 7-Eleven 联动已在 Steam 6.461.691.0 完成车池、函数特征与购买资格只读验证；
  使用者反馈购买闭环成功。
- 英国奇珍已在 Xbox 3.440.853.0 完成两个展位、22 辆车池及函数特征的只读验证。
- Steam 6.440.853.0 已完成英国两展位快速扫描、资格诊断与重购事务验证。
- 意大利奇珍已在 Xbox 应用版与 Steam 版完成三个展位切换实测。
- 意大利重购链路已在 Xbox 版 Abarth 595 esseesse、Steam 版 Aventador LP700-4 上完成
  “实际购买 -> 入口消失 -> 恢复资格 -> 临时换车 -> 完整刷新 -> 切回目标车”的实测。
- 每次游戏更新或新活动首次适配后，建议先用便宜车辆复测重购。
- 游戏失去焦点会打开暂停界面；完整刷新等待时仍应切回游戏。

日志与排错
==========
日志目录：%LOCALAPPDATA%\FH6RareCarShop\Logs

常见状态：
- “等待 Forza Horizon 6”：先启动游戏。
- “与所选活动不一致”：在顶栏选择游戏内当前开放的活动。
- “无法安全识别活动展位”：进入对应场地并等待所有模型加载完成。
- “目标展位交互尚未加载”：立即切回游戏并停留在活动场地。
- Steam 6.440.853.0 若购后入口消失，恢复重购会先通过资格对象反向关联重建 SaveState；不再使用旧版固定邻近引用。
- “未通过特征校验”：当前游戏版本已变化，工具会拒绝写入；请保留日志用于适配。
- Xbox 应用版的游戏 exe 不含版本资源，诊断页与日志中的版本号取自应用包身份。
- 等待完整刷新时间较长：切回游戏并保持在活动场地，不要停留在桌面。

部分安全软件可能将其归类为修改器；请只从可信来源获取，不要盲目关闭系统防护。

本工具为非官方社区工具。请自行遵守游戏服务条款，并自行承担使用风险。

项目主页、各活动适配记录与每个版本的 SHA-256 校验值：
https://github.com/zilewang7/FH6RareCarShop

许可证
======
MIT License

Copyright (c) 2026 FH6 Italian Rarities contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
