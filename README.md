# FH6 意大利奇珍库存控制器

Windows x64 下的 Forza Horizon 6 “意大利奇珍”活动库存选择与重购交互恢复工具。
当前版本为 **1.0.4**，已测试 Xbox 应用版与 Steam 版。

> **活动状态：**“意大利奇珍”活动已于 **2026-07-23 22:30（UTC+8）** 结束。
> 本项目开源时活动已经下线；除非活动未来返场，现成 EXE 无法在游戏内找到对应场地
> 和展位。源码仍可作为类似限时活动工具的参考实现与开发基座。
>
> 本项目是非官方社区工具，与 Playground Games、Xbox、Microsoft、Steam 及车辆品牌无关。
> 使用前请自行了解并遵守游戏服务条款，风险由使用者承担。

## 功能

- 从活动完整的 42 辆车辆目录中分别选择三个展位的车辆。
- 按原名、中文译名、品牌、年份、库存 ID 或车型 ID 搜索。
- 标注 6 辆抽奖限定车辆，并特别推荐 599XX Evolution 与 Sesto Elemento。
- 为购买后失去购买入口的车辆恢复重购交互。
- 车辆模型与购买入口同时消失时，自动通过中转车辆重建交互。
- 支持只读兼容性扫描、运行时结构校验和本地诊断日志。

对其他类似需求，可复用的部分包括：基于完整结构不变量的内存定位、已知版本画像与
未知版本回退、游戏内部函数的受校验调用、远程线程状态管理、事务式回滚，以及面向
非技术用户的 WPF 诊断界面。

## 与随机刷新的关系

这个工具不会修改存档、车库、货币或网络请求，也不会直接向账号发放车辆。普通库存
切换调用游戏自身的展位 setter；恢复重购还会调用游戏自身的完整刷新流程，所有候选车
仍受活动原有的 14 车展位池约束。

从当前游戏与工具日志能够观察到的结果看，游戏只是完成了一次正常活动刷新，并正好
“随机”到了所选车辆。其最终效果与完全退出游戏、重新进入并反复等待随机刷新没有
区别，工具只是省去了大退和等待时间。

截至已验证的游戏版本，Playground Games 未为 FH6 部署独立反作弊程序，测试中也未
观察到反作弊拦截。这不代表官方授权、永久兼容或零风险；游戏更新后，工具会在特征
校验失败时停止写入。

## 使用步骤

1. 从 [Releases](../../releases/latest) 下载最新的 `win-x64.zip` 并解压。
2. 启动 Forza Horizon 6，进入“意大利奇珍”活动场地，等待三个展位加载。
3. 以管理员身份运行 `FH6ItalianRarities.exe`。
4. 为每个展位选择车辆，点击“应用此展位”或“应用三个展位”。
5. 回到游戏查看刷新后的车辆。

工具需要管理员权限才能读取和调用游戏进程内的受保护接口。部分安全软件可能将进程
内存工具归类为修改器，请只从可信来源获取，不要为了运行它而盲目关闭系统防护。

## 恢复重购

仅在目标车辆已经购买、购买入口消失时使用：

1. 在对应展位选择刚买过的目标车辆，点击“恢复重购”。
2. 确认后立即切回游戏，并停留在活动场地。
3. 工具只检查目标展位；若交互已经卸载，会先切换中转车辆并等待重新加载。
4. 工具恢复资格、执行游戏完整刷新，再切回目标车辆。
5. 等待工具显示完成，然后在场地内确认购买入口。

完整刷新依赖游戏线程。等待期间不要强制关闭工具；退出游戏会清除所有进程内临时
状态。其他空展位不会阻断当前目标展位的恢复操作。

## 限定车辆

| 年份 | 品牌 | 车辆 | 备注 |
| --- | --- | --- | --- |
| 2012 | Ferrari | 599XX Evolution | 抽奖限定，特别推荐 |
| 2011 | Lamborghini | Sesto Elemento | 抽奖限定，特别推荐 |
| 1984 | Ferrari | 288 GTO | 抽奖限定 |
| 1999 | Lamborghini | Diablo GTR | 抽奖限定 |
| 2012 | Lamborghini | Aventador LP700-4 | 抽奖限定 |
| 2019 | Ferrari | F8 Tributo | 抽奖限定 |

## 安全边界

- 不读取或修改游戏存档，不抓取网络流量。
- 管理器、14 车池、车型映射、函数特征和资格 getter 必须全部通过校验。
- setter 与刷新函数必须在游戏模块内唯一匹配，否则拒绝调用。
- 临时车池和资格写入采用事务式回滚。
- 游戏线程状态未知时，不执行可能与游戏线程竞争的强制回滚。
- 管理器地址只在同一个进程生命周期内缓存，并在每次使用前重新验证。
- Release 构建使用路径映射，不会把构建者的本机源码路径写入异常堆栈。

更完整的技术过程见 [逆向与实现思路](docs/REVERSE_ENGINEERING.md)。

## 日志与隐私

日志目录：`%LOCALAPPDATA%\FH6ItalianRarities\Logs`

日志保留 14 天，包含游戏版本、发行渠道、进程 ID、运行时地址、扫描统计和异常信息，
不记录账号、存档内容或完整游戏安装路径。提交 Issue 前仍应自行检查日志，不要上传存档、
内存转储、访问令牌或其他个人文件。

## 从源码构建

要求：Windows x64、.NET 8 SDK、PowerShell。

```powershell
dotnet restore .\FH6ItalianRarities.sln
dotnet build .\src\FH6ItalianRarities\FH6ItalianRarities.csproj -c Release -r win-x64 -p:TreatWarningsAsErrors=true
dotnet .\src\FH6ItalianRarities\bin\Release\net8.0-windows\win-x64\FH6ItalianRarities.dll --self-test
```

生成自包含单文件分享包：

```powershell
.\src\FH6ItalianRarities\Publish-SharePackage.ps1 -Version 1.0.4
```

输出位于 `release/FH6-Italian-Rarities-v1.0.4-win-x64.zip`。

## 项目结构

- `src/FH6ItalianRarities`：WPF 界面与发布脚本。
- `src/FH6ItalianRarities.Core`：进程发现、内存校验、展位切换与重购事务。
- `docs/REVERSE_ENGINEERING.md`：从行为观测到稳定定位的逆向思路。
- `.github/workflows/build.yml`：Windows 构建与离线自检。

## 许可证

代码以 [MIT License](LICENSE) 发布。游戏名称、车辆名称和商标归各自权利人所有。
