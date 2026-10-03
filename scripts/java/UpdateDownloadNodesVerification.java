package cc.luoluoluo.yanzi.mobile;

import java.io.*;
import java.net.*;
import java.util.*;
import java.util.concurrent.atomic.AtomicInteger;

public final class UpdateDownloadNodesVerification {
    private static int assertions;
    private static void require(boolean value){assertions++;if(!value)throw new AssertionError("Scenario "+assertions);}
    public static void main(String[] args)throws Exception{
        String original="https://sync.luoluoluo.cc.cd/downloads/android/yanzi-mobile-0.2.45.apk";
        List<UpdateDownloadNodes.Node> candidates=UpdateDownloadNodes.candidates(original);
        require(candidates.size()==5);
        require(candidates.get(1).url.equals("https://github.com/luoluoluo22/yanzi/releases/download/android-v0.2.45/yanzi-mobile-0.2.45.apk"));
        require(candidates.stream().noneMatch(n->n.url.contains("kkgithub")));
        require(UpdateDownloadNodes.candidates("https://example.com/file.apk").size()==1);
        require(UpdateDownloadNodes.candidates("https://sync.luoluoluo.cc.cd/downloads/android/yanzi-mobile-0.2.45.apk?evil=1").size()==2);
        require(UpdateDownloadNodes.candidates(candidates.get(1).url).size()==3);
        byte[] apk=new byte[32768];apk[0]='P';apk[1]='K';apk[2]=3;apk[3]=4;
        require(UpdateDownloadNodes.apkSample(apk,apk.length,200,"application/octet-stream",null));
        require(UpdateDownloadNodes.apkSample(apk,apk.length,206,"application/vnd.android.package-archive","bytes 0-32767/90000000"));
        require(!UpdateDownloadNodes.apkSample(apk,100,200,null,null));
        require(!UpdateDownloadNodes.apkSample(apk,apk.length,206,null,"bytes 100-32867/90000000"));
        require(!UpdateDownloadNodes.apkSample(apk,apk.length,200,"text/html",null));
        require(!UpdateDownloadNodes.apkSample(apk,apk.length,429,null,null));
        byte[] html=apk.clone();html[0]='<';require(!UpdateDownloadNodes.apkSample(html,html.length,200,null,null));
        AtomicInteger closed=new AtomicInteger();
        List<UpdateDownloadNodes.Node> selected=UpdateDownloadNodes.select(original,(url,system)->new Fake(url,apk,url.getHost().equals("ghfast.top")?200:429,closed),()->false,s->{});
        require(selected.size()==1&&selected.get(0).name.equals("ghfast 镜像"));
        require(closed.get()>=5);
        require(UpdateDownloadNodes.select(original,(url,system)->new Fake(url,apk,429,closed),()->false,s->{}).get(0).url.equals(original));
        require(UpdateDownloadNodes.select(original,(url,system)->{throw new AssertionError("Canceled must not connect");},()->true,s->{}).isEmpty());
        require(UpdateDownloadNodes.select(original,(url,system)->{throw new SocketException("Connection reset");},()->false,s->{}).get(0).elapsedNanos==0);
        byte[] shortSample=Arrays.copyOf(apk,100);
        require(UpdateDownloadNodes.select(original,(url,system)->new Fake(url,shortSample,200,closed),()->false,s->{}).get(0).elapsedNanos==0);
        require(UpdateDownloadNodes.select(original,(url,system)->new Fake(url,html,200,closed),()->false,s->{}).get(0).elapsedNanos==0);
        List<UpdateDownloadNodes.Node> ranked=UpdateDownloadNodes.select(original,(url,system)->new Fake(url,apk,200,closed){
            @Override public InputStream getInputStream(){if(!url.getHost().equals("ghfast.top"))try{Thread.sleep(300);}catch(InterruptedException e){Thread.currentThread().interrupt();}return super.getInputStream();}
        },()->false,s->{});
        require(ranked.size()==5&&ranked.get(0).name.equals("ghfast 镜像"));
        long start=System.nanoTime();
        List<UpdateDownloadNodes.Node> stalled=UpdateDownloadNodes.select(original,(url,system)->new Fake(url,apk,200,closed){
            @Override public int getResponseCode(){try{Thread.sleep(10000);}catch(InterruptedException e){Thread.currentThread().interrupt();return 429;}return 200;}
        },()->false,s->{});
        require(stalled.get(0).elapsedNanos==0&&System.nanoTime()-start<java.util.concurrent.TimeUnit.SECONDS.toNanos(9));
        System.out.println("UPDATE_DOWNLOAD_NODES=PASSED ("+assertions+" scenarios)");
    }
    private static class Fake extends HttpURLConnection{
        final byte[] bytes;final int status;final AtomicInteger closed;
        Fake(URL url,byte[] bytes,int status,AtomicInteger closed){super(url);this.bytes=bytes;this.status=status;this.closed=closed;}
        public int getResponseCode(){return status;}public InputStream getInputStream(){return new ByteArrayInputStream(bytes);}
        public String getContentType(){return "application/octet-stream";}
        public void disconnect(){closed.incrementAndGet();}public void connect(){}public boolean usingProxy(){return false;}
    }
}
