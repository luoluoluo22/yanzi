package cc.luoluoluo.yanzi.mobile;

import java.util.ArrayList;
import java.util.HashSet;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Set;

/** Deterministic catalog order: retain the user's relative order and append newly discovered IDs. */
final class DesktopExtensionOrder {
    private DesktopExtensionOrder() {}

    static List<String> reconcile(List<String> stored, List<String> incoming) {
        LinkedHashSet<String> available = new LinkedHashSet<>();
        for (String id : incoming) if (id != null && !id.isEmpty()) available.add(id);
        Set<String> valid = new HashSet<>(available);
        LinkedHashSet<String> result = new LinkedHashSet<>();
        for (String id : stored) if (valid.contains(id)) result.add(id);
        result.addAll(available);
        return new ArrayList<>(result);
    }
}
