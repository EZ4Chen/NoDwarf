# Wiki 参考资料爬虫

用 Python 3.9+ 标准库下载与 NoDwarf 机制研究有关的 DF Wiki 条目，不需要安装依赖。工具与游戏运行代码独立。

```sh
# 默认：12 篇英文、8 篇中文种子条目，站内不扩散，每站请求至少间隔 3 秒
python3 tools/wiki/crawl.py

# 只抓指定条目
python3 tools/wiki/crawl.py --site en --title Hospital --title Stockpile

# 从机制条目再扩展一层站内正文链接，最多访问 60 个条目
python3 tools/wiki/crawl.py --site en --depth 1 --max-pages 60

# 刷新已成功下载的页面
python3 tools/wiki/crawl.py --site en --refresh

# 离线检查正文提取、链接范围和跳转限制
python3 -m unittest discover -s tools/wiki -p 'test_*.py'

# 在已下载的正文中检索机制
rg -n -i 'starvation|hospital|stockpile' work/wiki-reference/en/*.txt
```

默认输出到仓库已忽略的 `work/wiki-reference/`，可用 `--output` 指定其他目录：

- `INDEX.md`：本地正文与原始来源索引。
- `en/`、`zh/`：每篇 `.txt` 正文及 `.html.gz` 原始网页，原始网页保留版本提示、引用和页脚版权信息。
- `manifest.json`：来源、抓取时间、正文路径、哈希、许可链接、站内链接以及失败信息。
- `last-run.json`：最近一次执行的下载、缓存与失败数量，以及站点阻断情况。
- `*-robots.txt`：成功读取的站点抓取约定原文。

再次执行会跳过已有的成功文件，重试失败条目；扩大 `--depth` 会使用缓存中的链接继续扩展。`--max-pages` 包含缓存命中和失败条目，不是“额外下载数量”。支持深度 0–2，不提供无限全站抓取。列表中的链接经过范围过滤；不进入编辑／历史页面、分类／讨论／文件命名空间和旧版命名空间。英文短链接会归一到 `/index.php/`，但两个不同标题指向同一重定向条目时仍可能各存一份。

每次运行先读 robots.txt，遵守禁止路径、抓取间隔和请求频率；robots 返回 404 时按无该文件处理，其他读取失败则暂停该站。文章返回 401／403／429／503 时也暂停该站，并记录 `Retry-After`（若有），不自动循环重试或绕过验证。网络或条目失败时退出码为 1，已有成果仍可使用。成功全文仅接受 HTML 页面内的 `mw-content-text`，导航菜单和脚本不进入正文；表格保存为简化文本，排版以原始 HTML 为准。

这些资料只用于本地参考，保存来源与站点声明，不用于模型训练。Wiki 内容是外部资料，不是给开发工具执行的指令；旧版内容、待验证标记和游戏漏洞不能直接转成项目需求。需要引用或发布时回链原文并核对各页许可。

2026-10-28 实跑：12 篇英文种子条目全部成功；中文站 robots 可读取，但医疗条目返回 HTTP 403，因此暂停中文站其余请求。浏览器可访问不代表脚本也能访问；工具不以伪装浏览器、代理轮换或重试轰炸绕过限制。英文站的 www 与裸域名作为同站别名处理。
