package cc.luoluoluo.yanzi.mobile;
public final class MobileUpdateEntitlementVerification {
  private static void check(boolean value,String label){if(!value)throw new AssertionError(label);}
  public static void main(String[] args){
    check(MobileUpdateEntitlement.isFreeVersion("0.2.54"),"old Android 0.x free");
    check(MobileUpdateEntitlement.mayInstall("1.0.0",null,null,null),"free 1.0.0");
    check(!MobileUpdateEntitlement.mayInstall("1.0.1","2026-10-11T00:00:00Z",null,null),"non-VIP denied");
    check(!MobileUpdateEntitlement.mayInstall("1.0.1",null,"month","2026-10-12T00:00:00Z"),"unknown release date denied");
    check(MobileUpdateEntitlement.mayInstall("1.0.1","2026-10-10T00:00:00Z","month","2026-10-11T00:00:00Z"),"valid past release even after expiry");
    check(!MobileUpdateEntitlement.mayInstall("1.0.1","2026-10-12T00:00:00Z","month","2026-10-11T00:00:00Z"),"future paid release denied");
    check(MobileUpdateEntitlement.mayInstall("2.0.0",null,"lifetime",null),"lifetime");
    System.out.println("ANDROID_VIP_UPDATE_POLICY_PASSED");
  }
}
