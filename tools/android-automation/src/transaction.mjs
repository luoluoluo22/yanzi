import {verifyCartChange,cartKey} from './cart.mjs';
import {UnsafeTargetError} from './vision.mjs';

// Generic transaction guard. An actual UI adapter supplies readSnapshot and
// perform; this library never retries an action if outcome is uncertain.
export async function runVerifiedCartAction({type,target,delta=1,readSnapshot,perform}={}) {
  if(typeof readSnapshot!=='function'||typeof perform!=='function')throw new Error('Transaction adapters required');
  const before=await readSnapshot();
  if(!before?.verified)throw new UnsafeTargetError('Before snapshot is unverified; no UI action taken');
  const key=cartKey(target);
  let calls=0;
  try {
    calls++;
    await perform({type,target,delta});
  } catch(e) {
    return {status:'uncertain',attempts:calls,key,reason:'action_failed_or_timed_out',next:'reconcile_cart_before_any_retry'};
  }
  let after;
  try {after=await readSnapshot();}
  catch(e) {return {status:'uncertain',attempts:calls,key,reason:'post_action_snapshot_unavailable',next:'reconcile_cart_before_any_retry'};}
  try {
    const verification=verifyCartChange(before,after,{type,target,delta});
    return {status:'confirmed',attempts:calls,key,verification};
  } catch(e) {
    return {status:'uncertain',attempts:calls,key,reason:'cart_change_not_verified',next:'reconcile_cart_before_any_retry'};
  }
}
