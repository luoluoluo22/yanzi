import { config } from "../src/config.js";
try {
  const response=await fetch(`http://127.0.0.1:${config.port}/__raccoon/state`,{headers:{Authorization:`Bearer ${config.token}`},signal:AbortSignal.timeout(5000)});
  if(!response.ok) throw new Error("Current worker cannot confirm a safe stop; keep it running");
  const state=await response.json();
  console.log(JSON.stringify({safeToStop:!state.busy,state}));
  if(state.busy)process.exitCode=2;
} catch(error) {console.error(error.message);process.exitCode=2;}
