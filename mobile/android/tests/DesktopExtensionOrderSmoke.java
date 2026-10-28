package cc.luoluoluo.yanzi.mobile;

import java.util.Arrays;
import java.util.List;

public class DesktopExtensionOrderSmoke {
    private static void expect(List<String> actual, String... expected) {
        if (!actual.equals(Arrays.asList(expected)))
            throw new AssertionError("Expected " + Arrays.toString(expected) + ", got " + actual);
    }
    public static void main(String[] args) {
        expect(DesktopExtensionOrder.reconcile(List.of(), List.of("a","b","c")), "a","b","c");
        expect(DesktopExtensionOrder.reconcile(List.of("c","a","b"), List.of("b","a","c")), "c","a","b");
        expect(DesktopExtensionOrder.reconcile(List.of("c","a","b"), List.of("x","a","c","b","y")), "c","a","b","x","y");
        expect(DesktopExtensionOrder.reconcile(List.of("c","a","b"), List.of("b","c")), "c","b");
        expect(DesktopExtensionOrder.reconcile(List.of("c","b"), List.of("b","a","c")), "c","b","a");
        expect(DesktopExtensionOrder.reconcile(List.of("b","b","missing"), List.of("b","c","c")), "b","c");
        System.out.println("DESKTOP_EXTENSION_ORDER_SMOKE=PASS (initial, fixed order, appended, removed, re-added, duplicates)");
    }
}
