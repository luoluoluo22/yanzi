package cc.luoluoluo.yanzi.mobile;

import org.json.JSONObject;

final class AgentStreamProtocol {
    static final String PREFIX = "\u001eYANZI_STREAM:";

    static final class Event {
        final String id;
        final String conversationId;
        final String state;
        final int seq;
        final String text;

        Event(String id, String conversationId, String state, int seq, String text) {
            this.id = id;
            this.conversationId = conversationId;
            this.state = state;
            this.seq = seq;
            this.text = text;
        }
    }

    static Event parse(String content) {
        if (content == null || !content.startsWith(PREFIX)) return null;
        try {
            JSONObject value = new JSONObject(content.substring(PREFIX.length()));
            if (value.optInt("v", 0) != 1) return null;
            String id = value.optString("id", "").trim();
            String conversationId = value.optString("conversationId", "").trim();
            String state = value.optString("state", "").trim();
            int seq = value.optInt("seq", -1);
            String text = value.optString("text", "");
            if (id.length() < 8 || id.length() > 128 || conversationId.isEmpty() || conversationId.length() > 200
                    || seq < 0 || !(state.equals("start") || state.equals("update")
                    || state.equals("cancelled") || state.equals("error"))) return null;
            return new Event(id, conversationId, state, seq, text);
        } catch (Exception ignored) {
            return null;
        }
    }

    private AgentStreamProtocol() {}
}
