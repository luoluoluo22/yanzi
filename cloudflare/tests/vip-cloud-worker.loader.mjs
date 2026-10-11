// In tests only: emulate the Cloudflare module so Worker request handling can run under Node.
export async function resolve(specifier, context, nextResolve) {
  if (specifier === 'cloudflare:workers') {
    return {url:'data:text/javascript,export class DurableObject {}', shortCircuit:true};
  }
  return nextResolve(specifier, context);
}
