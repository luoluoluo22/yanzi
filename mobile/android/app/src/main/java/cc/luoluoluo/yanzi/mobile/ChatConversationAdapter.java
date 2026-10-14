package cc.luoluoluo.yanzi.mobile;
import android.content.Context;
import android.view.*;
import android.widget.*;
import java.util.List;

/** Contact rows share identity, preview and unread policy with the conversation view. */
final class ChatConversationAdapter extends BaseAdapter {
    private final Context context;
    private final List<ChatConversation> contacts;
    private final String selected;
    ChatConversationAdapter(Context context,List<ChatConversation> contacts,String selected) {
        this.context=context;this.contacts=contacts;this.selected=selected;
    }
    public int getCount(){return contacts.size();}
    public Object getItem(int position){return contacts.get(position);}
    public long getItemId(int position){return position;}
    public View getView(int position,View recycled,ViewGroup parent) {
        ChatConversation c=contacts.get(position);
        LinearLayout row=new LinearLayout(context);row.setGravity(Gravity.CENTER_VERTICAL);
        row.setPadding(dp(16),dp(12),dp(16),dp(12));
        row.setBackgroundColor(c.id.equals(selected)?YanziUiKit.CARD_ALT:YanziUiKit.CARD);
        ImageView icon=YanziUiKit.icon(context,c.agent?"rocket-launch":c.id.equals("group")?"account-group":c.platform.equals("desktop")?"desktop-classic":"cellphone",YanziUiKit.BLUE,24);
        row.addView(icon,new LinearLayout.LayoutParams(dp(40),dp(40)));
        LinearLayout copy=new LinearLayout(context);copy.setOrientation(LinearLayout.VERTICAL);
        TextView name=YanziUiKit.text(context,c.name,14,YanziUiKit.TEXT,true);name.setSingleLine(true);
        name.setEllipsize(android.text.TextUtils.TruncateAt.END);copy.addView(name);
        TextView preview=YanziUiKit.text(context,ChatHistoryStore.preview(context,c.id),12,YanziUiKit.SECONDARY,false);
        preview.setSingleLine(true);preview.setEllipsize(android.text.TextUtils.TruncateAt.END);
        LinearLayout.LayoutParams previewLp=new LinearLayout.LayoutParams(-1,-2);previewLp.topMargin=dp(5);copy.addView(preview,previewLp);
        LinearLayout.LayoutParams copyLp=new LinearLayout.LayoutParams(0,-2,1);copyLp.leftMargin=dp(10);row.addView(copy,copyLp);
        String prefix="chatObjects."+ChatHistoryStore.accountKey(context)+".seen.";
        int unread=ChatHistoryStore.unread(context,c.id,context.getSharedPreferences("yanzi-mobile",0).getLong(prefix+c.id,0));
        if(unread>0) {
            TextView badge=YanziUiKit.text(context,unread>99?"99+":Integer.toString(unread),11,YanziUiKit.TEXT,true);
            badge.setGravity(Gravity.CENTER);badge.setPadding(dp(6),dp(3),dp(6),dp(3));
            badge.setBackground(YanziUiKit.bg(YanziUiKit.RED,10,0,0));row.addView(badge);
        }
        return row;
    }
    private int dp(int size){return YanziUiKit.dp(context,size);}
}
