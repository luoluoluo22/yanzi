# 燕子：Bandizip 专有能力（2026-10-06）

## 已实现能力
- bandizip.status
- bandizip.compress
- bandizip.extract
- bandizip.test

## 本机环境
- Bandizip 6.25.0.1
- 可执行文件：C:\Program Files\Bandizip\Bandizip.exe
- 本机旧版本未提供独立 bz.exe，因此本阶段不提供依赖新版控制台程序的归档列表能力。

## 实现方式
全部通过 Bandizip 官方命令行接口执行，不做 GUI 坐标自动化。

- compress: c -y -l:<level> archive inputs...
- test: t -y archive
- extract: x -y -aoa/-aos -o:<dir> archive

通过 ProcessStartInfo.ArgumentList 传参并等待进程退出，最长 300 秒。

## 安全约束
- compress 输入必须真实存在，1-1000 项
- 已存在目标压缩包默认拒绝覆盖，需 overwrite=true
- extract 默认跳过已有文件；只有 overwrite=true 时覆盖
- 路径全部正规化
- 超时会结束 Bandizip 进程

## 真实验收
隔离目录：
artifacts/bandizip-capability-verify

创建：
- alpha.txt = yanzi-bandizip-alpha
- beta.txt = yanzi-bandizip-beta

通过正式 Capability API：
1. bandizip.compress -> exitCode=0，生成 verify.zip
2. bandizip.test -> valid=true
3. bandizip.extract -> exitCode=0
4. 解压后的两个文件内容与源文件逐字一致

Yanzi.CapabilityVerification：65/65。
