export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    // AI Proxy logic
    const pathParts = url.pathname.split("/").filter(Boolean);
    if (pathParts.length > 0) {
      const targetHost = pathParts[0];
      const restOfPath = "/" + pathParts.slice(1).join("/");
      
      const targetUrl = new URL(request.url);
      targetUrl.hostname = targetHost;
      targetUrl.pathname = restOfPath;
      targetUrl.protocol = "https:";
      targetUrl.port = "443";
      
      const headers = new Headers(request.headers);
      headers.set("Host", targetHost);
      
      if (request.method === "OPTIONS") {
          return new Response(null, {
              headers: {
                  "Access-Control-Allow-Origin": "*",
                  "Access-Control-Allow-Methods": "*",
                  "Access-Control-Allow-Headers": "*"
              }
          });
      }

      const newRequest = new Request(targetUrl.toString(), {
          method: request.method,
          headers: headers,
          body: request.method !== "GET" && request.method !== "HEAD" ? request.body : undefined,
          redirect: "manual"
      });

      try {
          let response = await fetch(newRequest);
          let responseHeaders = new Headers(response.headers);
          responseHeaders.set("Access-Control-Allow-Origin", "*");
          return new Response(response.body, {
              status: response.status,
              statusText: response.statusText,
              headers: responseHeaders
          });
      } catch (err) {
          return new Response(JSON.stringify({ error: err.message, target: targetUrl.toString() }), { status: 502 });
      }
    }

    return new Response("Yanzi AI Proxy Worker is running.", { status: 200 });
  }
};
