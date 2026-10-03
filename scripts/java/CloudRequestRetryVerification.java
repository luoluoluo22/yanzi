package cc.luoluoluo.yanzi.mobile;

import java.net.SocketException;

public final class CloudRequestRetryVerification {
    public static void main(String[] args) throws Exception {
        int[] attempts={0};
        String result=CloudRequestRetry.execute(true, system -> {attempts[0]++;if(!system)throw new SocketException("Connection reset");return "ok";});
        if(!"ok".equals(result)||attempts[0]!=2)throw new AssertionError("No route fallback");
        attempts[0]=0;
        try { CloudRequestRetry.execute(false, system -> {attempts[0]++;throw new SocketException("reset");}); throw new AssertionError("Unsafe request retried"); }
        catch(SocketException expected){if(attempts[0]!=1)throw new AssertionError("Duplicate unsafe operation");}
        attempts[0]=0;
        try { CloudRequestRetry.execute(true, system -> {attempts[0]++;throw new IllegalStateException("HTTP 403");});throw new AssertionError(); }
        catch(IllegalStateException expected){if(attempts[0]!=1)throw new AssertionError("Authorization failure retried");}
        attempts[0]=0;
        try { CloudRequestRetry.execute(true, system -> {attempts[0]++;throw new CloudRequestRetry.SessionChanged();});throw new AssertionError(); }
        catch(CloudRequestRetry.SessionChanged expected){if(attempts[0]!=1)throw new AssertionError("Old session retried after logout");}
        if(!CloudRequestRetry.safe("POST","/v1/me/devices",false)||!CloudRequestRetry.safe("POST","/v1/me/devices/lan-links",false)
                ||!CloudRequestRetry.safe("POST","/v1/me/mobile/messages",true)
                ||CloudRequestRetry.safe("POST","/v1/me/mobile/messages",false)
                ||CloudRequestRetry.safe("PUT","/v1/sync/objects/x",false)
                ||CloudRequestRetry.safe("POST","/v1/me/mobile/messages/x/claim",false))throw new AssertionError("Retry safety");
        System.out.println("PASS safe cloud route fallback, finite attempts, HTTP rejection and unsafe write isolation");
    }
}
