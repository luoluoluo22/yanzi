import asyncio
import json
import websockets

async def handler(websocket):
    print(f"\n[Mock Server] 客户端已连接!")
    try:
        async for message in websocket:
            data = json.loads(message)
            
            # 过滤心跳
            if data.get("type") == "ping":
                continue
                
            print(f"[Mock Server] 收到客户端数据: {data}")
            
            if data.get("type") == "register":
                # 发送握手成功应答
                ack = {"type": "register_ack", "status": "success"}
                await websocket.send(json.dumps(ack))
                print("[Mock Server] 握手注册成功!")
                
                # 3 秒后，下发核心的多步骤工作流 (Workflow) 自动化任务
                await asyncio.sleep(3)
                task = {
                    "type": "task_request",
                    "taskId": "task_workflow_demo_001",
                    "action": "workflow",
                    "url": "https://yanzi.luoluoluo.cc.cd/",
                    "closeOnComplete": False,  # 保持页面开启以供用户观看
                    "steps": [
                        # 1. 等待 H1 标题加载完成
                        {
                            "type": "wait",
                            "selector": "h1",
                            "timeout": 5000
                        },
                        # 2. 高保真自动填入官网演示搜索框
                        {
                            "type": "fill",
                            "selector": ".search-demo-input",
                            "value": "AI提效启动器"
                        },
                        # 3. 模拟点击激活搜索面板按钮
                        {
                            "type": "click",
                            "selector": ".search-trigger-btn"
                        },
                        # 4. 等待搜索栏动效与面板弹出完成
                        {
                            "type": "wait",
                            "selector": ".search-launcher",
                            "timeout": 3000
                        },
                        # 5. 执行多选择器数据抓取 (利用管道属性)
                        {
                            "type": "scrape",
                            "selectors": {
                                "main_title": "h1|innerText",
                                "hero_copy": ".hero-copy p.eyebrow|innerText",
                                "btn_text": ".search-trigger-btn|innerText"
                            }
                        },
                        # 6. 自动垂直滚动 500 像素以呈现内容
                        {
                            "type": "scroll",
                            "distance": 500
                        },
                        # 7. 静态延时 2 秒以方便预览
                        {
                            "type": "wait",
                            "timeout": 2000
                        }
                    ]
                }
                print(f"[Mock Server] 正在下发高级工作流自动化指令 [ID: {task['taskId']}] ...")
                await websocket.send(json.dumps(task))
                
            elif data.get("type") == "task_response":
                print(f"\n[Mock Server] 收到工作流执行结果 [ID: {data.get('taskId')}]")
                print(f"执行状态: {data.get('status')}")
                if data.get("message"):
                    print(f"错误信息: {data.get('message')}")
                if data.get("data"):
                    print("抓取到的累加数据:")
                    print(json.dumps(data.get("data"), ensure_ascii=False, indent=2))
                print("-" * 50)
                
    except websockets.exceptions.ConnectionClosedOK:
        print("[Mock Server] 客户端连接已正常断开。")
    except websockets.exceptions.ConnectionClosedError:
        print("[Mock Server] 客户端连接异常关闭。")
    except Exception as e:
        print(f"[Mock Server] 异常: {e}")

async def main():
    print("[Mock Server] 正在启动仿真本地 WebSocket 服务 (工作流测试版) ...")
    print("服务地址: ws://127.0.0.1:18293")
    async with websockets.serve(handler, "127.0.0.1", 18293):
        await asyncio.Future()  # 持续运行

if __name__ == "__main__":
    asyncio.run(main())
