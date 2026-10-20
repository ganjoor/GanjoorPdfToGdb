# Local mock of an OpenAI-compatible /v1/chat/completions endpoint. SYNTHETIC answers keyed by PDF page index.
import json, re, sys
from http.server import BaseHTTPRequestHandler, HTTPServer

def row(r, l, m=None): 
    d = {"type":"verse_row","layout":"two_column","right":r,"left":l}
    if m: d["marginNumber"]=m
    return d
def c(t, m=None):
    d = {"type":"verse_row","layout":"centered","text":t}
    if m: d["marginNumber"]=m
    return d

PAGES = {
 9:  {"pageKind":"prose","printedPageLabel":"هشت","runningHeader":"دیوان طالب آملی","blocks":[]},
 76: {"pageKind":"poetry","runningHeader":"قصاید","blocks":[
        {"type":"category_heading","text":"قصاید"},
        {"type":"poem_heading","text":"قصیده در مدح اعتماد الدوله"},
        row("الف ب پ ت","ث ج چ حاست"), row("ک گ ل م","ن و ه حاست"), row("ص ض ط ظ","ع غ ف حاست")]},
 600:{"pageKind":"poetry","printedPageLabel":"۵۲۷","runningHeader":"غزلیات","blocks":[
        {"type":"category_heading","text":"غزلیات"},
        {"type":"poem_heading","number":"782"},
        row("برقص آن نازنین هرگه قدم از ناز بردارد","دلم درسینه چون خلخال او آواز بردارد","11530"),
        row("گذارد آن گل از شیرینی افسانه‌ام هردم","سریر عشوه بر بالین ناز و باز بردارد"),
        row("مبادا سحر چشمت پردة اعجاز (۱) بردارد","دلم بیتابی از حد میبرد در نوحه‌پردازی"),
        row("ندارد صبر چندانی که مطرب‌ساز بردارد","دلم بیتابی از حد می‌برد در نوحه‌پردازی"),
        row("عجب نبود که با این اضطراب شوق عضوم","شکوه عشق جو مرغان سبک پرواز بردارد"),
        c("یکی برکنده عشقی ز آشیان افتاده‌ام «طالب»"), c("مگر از خاک عجزم چنگل شهباز بردارد","11535"),
        {"type":"poem_heading","number":"783"},
        row("سخن سرکن که طعم شکر از نوش تو برخیزد","بعل بگشاکه بوی گل ز آغوش تو برخیزد"),
        row("جهان شفق گیرد چوسر بر پوش تو برخیزد","تو زیر پرده با آن لعل لب درجی زیاقوتی"),
        {"type":"footnote","text":"(۱) نسخه بدل: اعجاز"}]},
 601:{"pageKind":"poetry","runningHeader":"غزلیات","startsMidPoem":True,"blocks":[
        row("عجب گر دانه شایسته گوش تو برخیزد","اگر صد گوهر نایاب را یکیک نگین سازند"),
        row("گر ازپی نوش بادی ازلب نوش تو برخیزد","نشینده شعله جوم سروقبا پوش تو برخیزد","11999"),
        row("چرا بروی کمان از گوشه دوش تو برخیزد","کمین برمردم چشم غزال قدس بگشاید"),
        c("هزاران دل بخون غلطان شود اول دل «طالب»"), c("چو مهر دلبری از لعل خاموش تو برخیزد")]},
}
class H(BaseHTTPRequestHandler):
    def do_POST(self):
        n = int(self.headers['Content-Length']); body = json.loads(self.rfile.read(n))
        auth = self.headers.get('Authorization','')
        parts = body['messages'][1]['content']
        text = parts[0]['text']; img = parts[1]['image_url']['url']
        page = int(re.search(r'PDF page index: (\d+)', text).group(1))
        print(f"page={page} auth={'yes' if auth.startswith('Bearer ') else 'no'} img_b64_len={len(img)} json_mode={'response_format' in body} model={body['model']}", file=sys.stderr, flush=True)
        if page == 9999:   # simulate rejection of response_format
            pass
        out = json.dumps(PAGES.get(page, {"pageKind":"blank","blocks":[]}), ensure_ascii=False)
        if page == 601: out = "```json\n" + out + "\n```"   # models like to add fences
        resp = {"choices":[{"message":{"role":"assistant","content":out}}],"usage":{"prompt_tokens":1500,"completion_tokens":400}}
        data = json.dumps(resp).encode()
        self.send_response(200); self.send_header('Content-Type','application/json'); self.send_header('Content-Length',str(len(data))); self.end_headers(); self.wfile.write(data)
    def log_message(self,*a): pass
HTTPServer(('127.0.0.1', 8099), H).serve_forever()
