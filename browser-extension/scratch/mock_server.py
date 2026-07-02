import asyncio
import json
import websockets

async def handler(websocket):
    print(f"\n[Mock Server] 客户端已连接!")
    try:
        async for message in websocket:
            data = json.loads(message)
            
            # 过滤心跳包
            if data.get("type") == "ping":
                continue
                
            print(f"[Mock Server] 收到客户端数据: {data}")
            
            if data.get("type") == "register":
                # 发送注册确认
                ack = {"type": "register_ack", "status": "success"}
                await websocket.send(json.dumps(ack))
                print("[Mock Server] 握手注册成功!")
                
                # 3 秒后下发第一个任务：抓取官网数据并关闭页面
                await asyncio.sleep(3)
                task1 = {
                    "type": "task_request",
                    "taskId": "task_scrape_001",
                    "action": "scrape",
                    "url": "https://yanzi.luoluoluo.cc.cd/",
                    "selectors": {
                        "main_title": "h1|innerText",
                        "logo_src": ".brand-logo|src",
                        "nav_links": "nav a|href"
                    },
                    "closeOnComplete": True
                }
                print(f"[Mock Server] 下发数据抓取任务: {task1['taskId']}")
                await websocket.send(json.dumps(task1))
                
            elif data.get("type") == "task_response":
                print(f"[Mock Server] 收到任务 [ID: {data.get('taskId')}] 响应状态: {data.get('status')}")
                if data.get("status") == "success" and data.get("data"):
                    print(f"[Mock Server] 抓取到的数据:")
                    print(json.dumps(data.get("data"), ensure_ascii=False, indent=2))
                
                # 第一个抓取任务成功后，隔 4 秒下发第二个任务：自动填表并点击测试（保持页面开启）
                if data.get("taskId") == "task_scrape_001":
                    await asyncio.sleep(4)
                    task2 = {
                        "type": "task_request",
                        "taskId": "task_autofill_002",
                        "action": "autofill",
                        "url": "https://yanzi.luoluoluo.cc.cd/",
                        "fields": [
                            { "selector": ".search-demo-input", "value": "AI 效率神器" }
                        ],
                        "clickSelector": ".search-trigger-btn",
                        "closeOnComplete": False
                    }
                    print(f"[Mock Server] 下发网页表单自动填充任务: {task2['taskId']}")
                    await websocket.send(json.dumps(task2))
                    
    except websockets.exceptions.ConnectionClosedOK:
        print("[Mock Server] 客户端连接已正常断开。")
    except websockets.exceptions.ConnectionClosedError:
        print("[Mock Server] 客户端连接异常关闭。")
    except Exception as e:
        print(f"[Mock Server] 异常: {e}")

async def main():
    print("[Mock Server] 正在启动仿真本地 WebSocket 服务...")
    print("服务地址: ws://127.0.0.1:18293")
    async with websockets.serve(handler, "127.0.0.1", 18293):
        await asyncio.Future()  # 持续运行

if __name__ == "__main__":
    asyncio.run(main())
