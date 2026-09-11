using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace EmergencyArchive.Sync;

/// <summary>
/// Renders a <see cref="CredentialDatabase"/> into a single self-contained HTML
/// page (inline CSS + JS, no external references, no network) shown in the
/// in-app viewer. Pure and cross-platform so it is fully unit-testable; the
/// Windows-only WebView2 host merely displays the string this produces.
///
/// Security posture:
///  - Credential data is emitted as a JSON island (System.Text.Json with strict
///    encoding), never interpolated into markup, so values cannot break out of
///    context. The script renders values via <c>textContent</c> (never
///    <c>innerHTML</c>), so a hostile site/username/value cannot inject script.
///  - A strict Content-Security-Policy forbids all network and external
///    resources; only the inline style/script run.
/// </summary>
public static class CredentialPageBuilder
{
    /// <summary>Builds the full HTML document for the given credentials.</summary>
    public static string Build(CredentialDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        // Serialize with the strict HTML-safe encoder: <, >, &, ', " and any
        // non-ASCII are \u-escaped, so the JSON island cannot terminate the
        // <script> block or inject markup.
        // JavaScriptEncoder.Default is the strict, HTML-safe encoder: it
        // \u-escapes <, >, &, ', " and non-ASCII, so the JSON island cannot
        // terminate the <script> block or inject markup.
        var options = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.Default,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        object payload = new
        {
            entries = database.Entries.Select(c => new
            {
                site = c.Site,
                username = c.Username,
                password = c.Password,
                extras = c.EffectiveExtras.Select(e => new { key = e.Key, value = e.Value }),
            }),
        };

        string json = JsonSerializer.Serialize(payload, options);

        // Extra safety: neutralize any "</script" sequence inside the JSON so it
        // can never close the embedding script element (the strict encoder
        // already escapes '<', but this is defense in depth and cheap).
        json = json.Replace("</", "<\\/", StringComparison.Ordinal);

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"")
          .Append("default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; img-src data:; base-uri 'none'; form-action 'none'")
          .Append("\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("<title>Passwords</title>");
        sb.Append("<style>").Append(Css).Append("</style>");
        sb.Append("</head><body>");
        sb.Append("<h1>Passwords</h1>");
        sb.Append("<input id=\"q\" type=\"search\" placeholder=\"Search by site or username…\" autocomplete=\"off\" spellcheck=\"false\">");
        sb.Append("<p id=\"summary\" class=\"muted\"></p>");
        sb.Append("<div id=\"list\"></div>");
        sb.Append("<script id=\"data\" type=\"application/json\">").Append(json).Append("</script>");
        sb.Append("<script>").Append(Script).Append("</script>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private const string Css = """
        :root{color-scheme:light dark}
        body{font-family:'Inter',system-ui,Segoe UI,sans-serif;max-width:900px;margin:0 auto;padding:16px;}
        h1{font-size:20px;font-weight:600;margin:8px 0 12px}
        #q{width:100%;box-sizing:border-box;font-size:16px;padding:10px 12px;border-radius:8px;border:1px solid #8884;margin-bottom:8px}
        .muted{opacity:.65;font-size:12px;margin:4px 0 12px}
        .card{border:1px solid #8884;border-radius:10px;padding:12px 14px;margin:8px 0}
        .site{font-weight:600;font-size:15px}
        .row{display:flex;align-items:center;gap:8px;margin-top:8px}
        .label{width:90px;opacity:.7;font-size:12px}
        .val{font-family:ui-monospace,Consolas,monospace;flex:1;overflow-wrap:anywhere}
        .btn{cursor:pointer;border:1px solid #8884;background:transparent;border-radius:6px;padding:3px 8px;font-size:12px}
        .btn:hover{background:#8882}
        .extras{margin-top:8px;border-top:1px solid #8883;padding-top:8px;display:none}
        .extras.open{display:block}
        .toggle{font-size:12px;opacity:.8;cursor:pointer;margin-top:8px;user-select:none}
        .copied{color:#2a2}
        """;

    // Reads the JSON island, renders via textContent only, and wires
    // search / reveal / copy. No secret is ever placed into innerHTML.
    private const string Script = """
        (function(){
          var data = JSON.parse(document.getElementById('data').textContent||'{"entries":[]}');
          var entries = (data.entries||[]);
          var list = document.getElementById('list');
          var summary = document.getElementById('summary');
          var q = document.getElementById('q');

          function el(tag, cls, text){var e=document.createElement(tag); if(cls)e.className=cls; if(text!=null)e.textContent=text; return e;}

          function copyText(t, btn){
            navigator.clipboard && navigator.clipboard.writeText(t).then(function(){
              var old=btn.textContent; btn.textContent='copied'; btn.classList.add('copied');
              setTimeout(function(){btn.textContent=old; btn.classList.remove('copied');},1200);
            });
          }

          function secretRow(label, value){
            var row=el('div','row');
            row.appendChild(el('span','label',label));
            var val=el('span','val'); var shown=false;
            function render(){ val.textContent = shown ? value : '\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022'; }
            render();
            row.appendChild(val);
            var eye=el('button','btn','reveal');
            eye.addEventListener('click',function(){shown=!shown; eye.textContent=shown?'hide':'reveal'; render();});
            var copy=el('button','btn','copy');
            copy.addEventListener('click',function(){copyText(value,copy);});
            row.appendChild(eye); row.appendChild(copy);
            return row;
          }

          function plainRow(label, value){
            var row=el('div','row');
            row.appendChild(el('span','label',label));
            var val=el('span','val',value);
            row.appendChild(val);
            var copy=el('button','btn','copy');
            copy.addEventListener('click',function(){copyText(value,copy);});
            row.appendChild(copy);
            return row;
          }

          function card(entry){
            var c=el('div','card');
            c.appendChild(el('div','site',entry.site||'(no site)'));
            c.appendChild(plainRow('Username', entry.username||''));
            c.appendChild(secretRow('Password', entry.password||''));
            var extras=(entry.extras||[]);
            if(extras.length){
              var toggle=el('div','toggle','\u25B8 '+extras.length+' more field(s)');
              var box=el('div','extras');
              extras.forEach(function(x){ box.appendChild(secretRow(x.key||'', x.value||'')); });
              toggle.addEventListener('click',function(){
                var open=box.classList.toggle('open');
                toggle.textContent=(open?'\u25BE ':'\u25B8 ')+extras.length+' more field(s)';
              });
              c.appendChild(toggle); c.appendChild(box);
            }
            return c;
          }

          function matches(entry, term){
            if(!term) return true;
            term=term.toLowerCase();
            return (entry.site||'').toLowerCase().indexOf(term)>=0 ||
                   (entry.username||'').toLowerCase().indexOf(term)>=0;
          }

          function render(){
            var term=q.value.trim();
            list.textContent='';
            var shown=0;
            entries.forEach(function(e){ if(matches(e,term)){ list.appendChild(card(e)); shown++; } });
            summary.textContent = shown+' of '+entries.length+' credential(s)';
          }

          q.addEventListener('input', render);
          render();
        })();
        """;
}
