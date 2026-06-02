// ===== PBAv2 Content Studio — Editor (creation flow) =====
const { useState: useStateEd, useRef: useRefEd, useEffect: useEffectEd } = React;

const STAGE_ORDER = ['Idea', 'Draft', 'Review', 'Approved', 'Scheduled', 'Published'];

// status -> the actions shown in the editor action bar (mirrors content-editor.component.ts)
function stageActions(status) {
  switch (status) {
    case 'Idea': return [{ label: 'Start draft', kind: 'primary', to: 'Draft', ai: true }];
    case 'Draft': return [
      { label: 'Save draft', kind: 'ghost' },
      { label: 'Submit for review', kind: 'ghost', to: 'Review' },
      { label: 'Approve', kind: 'primary', to: 'Approved' },
    ];
    case 'Review': return [
      { label: 'Request changes', kind: 'ghost', to: 'Draft' },
      { label: 'Approve', kind: 'primary', to: 'Approved' },
    ];
    case 'Approved': return [
      { label: 'Schedule', kind: 'ghost', publish: 'schedule' },
      { label: 'Publish', kind: 'primary', publish: 'publish' },
    ];
    case 'Scheduled': return [{ label: 'Unschedule', kind: 'ghost', to: 'Approved' }, { label: 'Publish now', kind: 'primary', publish: 'publish' }];
    case 'Published': return [{ label: 'Open published', kind: 'ghost' }, { label: 'Create variant', kind: 'ghost' }];
    default: return [];
  }
}

// canned AI replies keyed by quick action
const AI_REPLIES = {
  draft: "Here's a first draft built from your idea and tuned to your voice — direct, a little contrarian, no throat-clearing. I opened on the tension and saved the takeaway for the last line.",
  refine: "Tightened the second section and cut three hedging phrases. Your sentences hit harder when they don't apologize for themselves.",
  shorten: "Trimmed ~40% — kept the hook, the three lessons, and the closing line. Reads in under a minute now.",
  expand: "Added a concrete example to the 'scaffolding' section and one more lesson. Gives skimmers something to stop on.",
  tone: "Rewrote in a warmer, more conversational register — same backbone, less lecture. Want me to push it further?",
};

function VoiceMeter({ score }) {
  const c = scoreColor(score);
  const pct = score == null ? 0 : score;
  return (
    <div className="vmeter">
      <div className="vmeter-top">
        <span className="vmeter-label">Voice match</span>
        <span className="vmeter-val" style={{ color: c }}>{score == null ? '—' : score}</span>
      </div>
      <div className="vmeter-track"><div className="vmeter-fill" style={{ width: pct + '%', background: c }} /></div>
      <p className="vmeter-note">
        {score == null ? 'Draft something to check your voice.'
          : score >= 80 ? 'Sounds like you. Confident and specific.'
          : score >= 60 ? 'Close — a few phrases feel generic.'
          : 'Reads a bit flat. Try the Refine action.'}
      </p>
    </div>
  );
}

function AISidecar({ open, onClose, status }) {
  const noBody = status === 'Idea';
  const chips = noBody
    ? [['draft', 'Draft from idea'], ['draft', 'Draft from scratch']]
    : [['refine', 'Refine'], ['shorten', 'Shorten'], ['expand', 'Expand'], ['tone', 'Change tone']];

  const [msgs, setMsgs] = useStateEd([
    { role: 'assistant', text: noBody ? "Tell me the angle, or hit Draft and I'll turn this idea into a first pass in your voice." : "I've read the draft. Want me to tighten it, change the tone, or pull a LinkedIn version out of it?" },
  ]);
  const [input, setInput] = useStateEd('');
  const [thinking, setThinking] = useStateEd(false);
  const endRef = useRefEd(null);

  useEffectEd(() => { if (endRef.current) endRef.current.scrollTop = endRef.current.scrollHeight; }, [msgs, thinking]);

  const ask = (text, key) => {
    if (!text.trim()) return;
    setMsgs((m) => [...m, { role: 'user', text }]);
    setInput('');
    setThinking(true);
    setTimeout(() => {
      setThinking(false);
      const reply = AI_REPLIES[key] || "Done — applied that to the draft. Take a look and let me know what to adjust.";
      setMsgs((m) => [...m, { role: 'assistant', text: reply, applic: true }]);
    }, 900);
  };

  if (!open) return null;
  return (
    <aside className="ai-sidecar">
      <header className="ai-head">
        <span className="ai-title"><span className="ai-spark">✦</span> Assistant</span>
        <button className="x" onClick={onClose}>✕</button>
      </header>
      <div className="ai-msgs" ref={endRef}>
        {msgs.map((m, i) => (
          <div key={i} className={'ai-msg ' + m.role}>
            <div className="ai-bubble">{m.text}</div>
            {m.applic && (
              <div className="ai-actions">
                <button className="ai-act">✓ Apply to draft</button>
                <button className="ai-act">⧉ Copy</button>
              </div>
            )}
          </div>
        ))}
        {thinking && (
          <div className="ai-msg assistant">
            <div className="ai-bubble ai-think"><span></span><span></span><span></span></div>
          </div>
        )}
      </div>
      <div className="ai-chips">
        {chips.map(([key, label], i) => (
          <button key={i} className="ai-chip" onClick={() => ask(label, key)} disabled={thinking}>{label}</button>
        ))}
      </div>
      <div className="ai-input">
        <textarea rows="1" value={input} placeholder="Ask the assistant…"
          onChange={(e) => setInput(e.target.value)}
          onKeyDown={(e) => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); ask(input, 'chat'); } }} />
        <button className="ai-send" onClick={() => ask(input, 'chat')} disabled={!input.trim() || thinking}>↑</button>
      </div>
    </aside>
  );
}

function Editor({ item, onBack, onPublish, onAdvance, flash }) {
  const [aiOpen, setAiOpen] = useStateEd(true);
  const [score, setScore] = useStateEd(item?.voiceScore ?? (item?.status === 'Idea' ? null : 72));
  const title = item?.title || SAMPLE_TITLE;
  const status = item?.status || 'Draft';
  const platform = item?.primaryPlatform || 'Blog';
  const type = item?.type || 'Blog';
  const targets = item?.targetPlatforms || ['Blog'];
  const actions = stageActions(status);
  const stageIdx = STAGE_ORDER.indexOf(status);
  const isIdea = status === 'Idea';

  const onAction = (a) => {
    if (a.publish) { onPublish(item, a.publish); return; }
    if (a.to) {
      if (a.ai) { setScore(74); flash('Drafted with your voice — score 74'); }
      onAdvance(item, a.to);
      flash(`Moved to ${a.to}`);
    } else {
      flash(a.label);
    }
  };

  return (
    <div className="editor-screen">
      <header className="ed-top">
        <button className="ed-back" onClick={onBack}>← Studio</button>
        <div className="ed-stage">
          {STAGE_ORDER.map((s, i) => (
            <React.Fragment key={s}>
              <span className={'ed-stage-dot' + (i <= stageIdx ? ' on' : '') + (i === stageIdx ? ' cur' : '')}
                style={i === stageIdx ? { background: STATUS_META[s].color, borderColor: STATUS_META[s].color } : {}}
                title={s} />
              {i < STAGE_ORDER.length - 1 && <span className={'ed-stage-line' + (i < stageIdx ? ' on' : '')} />}
            </React.Fragment>
          ))}
          <span className="ed-stage-label" style={{ color: STATUS_META[status]?.color }}>{status}</span>
        </div>
        <div className="ed-top-right">
          <span className="ed-meta"><span className="type-glyph sm">{TYPE_META[type].glyph}</span>{TYPE_META[type].label} · {platform}</span>
          <span className="ed-save">Saved</span>
          <VoiceScore score={score} size={32} />
          {!aiOpen && <button className="btn-ghost sm" onClick={() => setAiOpen(true)}><span className="ai-spark">✦</span> Assistant</button>}
        </div>
      </header>

      <div className="ed-body">
        <div className="ed-scroll">
          <div className="manuscript">
            <div className="ms-tags">{(item?.tags || ['writing', 'habits']).map((t) => <span key={t} className="tag">#{t}</span>)}</div>
            <h1 className="ms-title" contentEditable suppressContentEditableWarning>{title}</h1>
            <p className="ms-sub" contentEditable suppressContentEditableWarning>{SAMPLE_SUBTITLE}</p>
            <div className="ms-byline"><span className="avatar sm">JL</span> Jordan Lee · draft</div>
            {isIdea ? (
              <div className="ms-idea">
                <div className="ms-idea-mark">✦</div>
                <p className="ms-idea-h">This is still just an idea.</p>
                <p className="ms-idea-p">Hit <b>Start draft</b> below, or open the assistant and tell it the angle — it'll turn this into a first pass in your voice.</p>
              </div>
            ) : (
              <div className="ms-prose" contentEditable suppressContentEditableWarning onInput={() => setScore((s) => Math.min(96, (s || 70) + 1))}>
                {SAMPLE_BODY.map((b, i) =>
                  b.type === 'h2' ? <h2 key={i}>{b.text}</h2> : <p key={i}>{b.text}</p>
                )}
              </div>
            )}
          </div>
        </div>

        {aiOpen && (
          <div className="ed-side">
            <div className="ed-side-voice"><VoiceMeter score={score} /></div>
            <AISidecar open={aiOpen} onClose={() => setAiOpen(false)} status={status} />
          </div>
        )}
      </div>

      <footer className="ed-actions">
        <div className="ed-actions-left">
          <span className="ed-targets-label">Targets</span>
          <PlatformRow platforms={targets} max={6} />
        </div>
        <div className="ed-actions-right">
          {actions.map((a, i) => (
            <button key={i} className={a.kind === 'primary' ? 'btn-primary' : 'btn-ghost'} onClick={() => onAction(a)}>
              {a.label}{a.publish === 'publish' ? ' →' : ''}
            </button>
          ))}
        </div>
      </footer>
    </div>
  );
}

Object.assign(window, { Editor, AISidecar, VoiceMeter });
