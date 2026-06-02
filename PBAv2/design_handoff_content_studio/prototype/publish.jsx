// ===== PBAv2 Content Studio — multi-format Publish preview =====
const { useState: useStatePub, useMemo: useMemoPub } = React;

// ---------- per-platform preview renderers ----------
function ProseBlocks({ cls }) {
  return SAMPLE_BODY.map((b, i) =>
    b.type === 'h2'
      ? <h3 key={i} className={cls + '-h'}>{b.text}</h3>
      : <p key={i} className={cls + '-p'}>{b.text}</p>);
}

function BlogPreview() {
  return (
    <div className="pv pv-blog">
      <div className="pv-hero" />
      <div className="pv-blog-body">
        <span className="pv-kicker">Essays</span>
        <h1>{SAMPLE_TITLE}</h1>
        <p className="pv-lede">{SAMPLE_SUBTITLE}</p>
        <div className="pv-byline"><span className="avatar sm">JL</span> Jordan Lee · 4 min read</div>
        <ProseBlocks cls="pvb" />
      </div>
    </div>
  );
}

function MediumPreview() {
  return (
    <div className="pv pv-medium">
      <h1>{SAMPLE_TITLE}</h1>
      <p className="pv-medium-sub">{SAMPLE_SUBTITLE}</p>
      <div className="pv-medium-author">
        <span className="avatar sm">JL</span>
        <div><div className="pvm-name">Jordan Lee</div><div className="pvm-meta">4 min read · Member-only</div></div>
        <button className="pvm-follow">Follow</button>
      </div>
      <div className="pv-medium-bar"><span>👏 128</span><span>💬 14</span><span className="pvm-spacer" /><span>⤴</span><span>🔖</span></div>
      <ProseBlocks cls="pvm" />
    </div>
  );
}

function SubstackPreview() {
  return (
    <div className="pv pv-sub">
      <div className="pv-sub-masthead">
        <div className="pv-sub-pub">The Quiet Build</div>
        <button className="pv-sub-cta">Subscribe</button>
      </div>
      <div className="pv-sub-body">
        <h1>{SAMPLE_TITLE}</h1>
        <p className="pv-sub-deck">{SAMPLE_SUBTITLE}</p>
        <div className="pv-byline"><span className="avatar sm">JL</span> Jordan Lee · to 4,210 subscribers</div>
        <ProseBlocks cls="pvs" />
        <div className="pv-sub-foot">You're receiving this because you subscribed to The Quiet Build. <u>Unsubscribe</u></div>
      </div>
    </div>
  );
}

function LinkedInPreview() {
  const text = bodyPlainText();
  const limit = 210;
  const truncated = text.length > limit;
  const shown = truncated ? text.slice(0, limit).trimEnd() : text;
  return (
    <div className="pv pv-li">
      <div className="pv-li-card">
        <div className="pv-li-head">
          <span className="avatar">JL</span>
          <div className="pv-li-id">
            <div className="pv-li-name">Jordan Lee</div>
            <div className="pv-li-sub">Writer · Building in public</div>
            <div className="pv-li-time">1h · 🌐</div>
          </div>
          <span className="pv-li-dots">···</span>
        </div>
        <div className="pv-li-text">{shown}{truncated && <span className="pv-li-more">…more</span>}</div>
        <div className="pv-li-stats"><span>👍❤️💡 86</span><span>14 comments</span></div>
        <div className="pv-li-bar"><span>👍 Like</span><span>💬 Comment</span><span>↻ Repost</span><span>➤ Send</span></div>
      </div>
    </div>
  );
}

function TwitterPreview() {
  const chunks = useMemoPub(() => splitThread(bodyPlainText(), 250), []);
  return (
    <div className="pv pv-tw">
      {chunks.map((c, i) => (
        <div key={i} className="pv-tw-tweet">
          <div className="pv-tw-rail"><span className="avatar sm">JL</span>{i < chunks.length - 1 && <span className="pv-tw-thread" />}</div>
          <div className="pv-tw-main">
            <div className="pv-tw-id"><b>Jordan Lee</b> <span>@jordanwrites · {i === 0 ? '1m' : ''}</span></div>
            <div className="pv-tw-text">{c} <span className="pv-tw-num">{i + 1}/{chunks.length}</span></div>
            <div className="pv-tw-bar"><span>💬</span><span>↻</span><span>♡</span><span>↑</span></div>
          </div>
        </div>
      ))}
    </div>
  );
}

const PREVIEWS = { Blog: BlogPreview, Medium: MediumPreview, Substack: SubstackPreview, LinkedIn: LinkedInPreview, Twitter: TwitterPreview };

// ---------- delivery badge ----------
function DeliveryBadge({ p }) {
  const m = PLATFORM_META[p];
  if (m.delivery === 'auto' && m.connected) return <span className="db db-auto">⚡ Auto-publish</span>;
  if (m.delivery === 'auto' && !m.connected) return <span className="db db-warn">⚡ Connect to auto-publish</span>;
  return <span className="db db-manual">✋ Manual</span>;
}

// ---------- destination row ----------
function DestRow({ p, primary, selected, onToggle }) {
  const m = PLATFORM_META[p];
  const len = bodyPlainText().length;
  return (
    <label className={'dest' + (selected ? ' on' : '') + (primary ? ' primary' : '')}>
      <input type="checkbox" checked={selected} disabled={primary} onChange={onToggle} />
      <span className="dest-dot" style={{ color: m.color, borderColor: m.color + '66' }}>{m.code}</span>
      <span className="dest-main">
        <span className="dest-name">{m.label}{primary && <span className="dest-primary">Primary</span>}</span>
        <span className="dest-fmt">{m.fmt}</span>
      </span>
      <span className="dest-right">
        <DeliveryBadge p={p} />
        {m.charLimit && <span className={'dest-chars' + (len > m.charLimit ? ' over' : '')}>{p === 'Twitter' ? splitThread(bodyPlainText(), 250).length + ' tweets' : len + '/' + m.charLimit}</span>}
      </span>
    </label>
  );
}

function PublishOverlay({ item, mode, onClose, onPublished }) {
  const primary = item?.primaryPlatform && PUBLISHABLE.includes(item.primaryPlatform) ? item.primaryPlatform : 'Blog';
  const initial = Array.from(new Set([primary, ...(item?.targetPlatforms || []).filter((p) => PUBLISHABLE.includes(p))]));
  const [selected, setSelected] = useStatePub(initial);
  const [tab, setTab] = useStatePub(primary);
  const [scheduledAt, setScheduledAt] = useStatePub('');
  const [result, setResult] = useStatePub(null); // null | array of {p, state}

  const toggle = (p) => setSelected((s) => s.includes(p) ? s.filter((x) => x !== p) : [...s, p]);

  const autoCount = selected.filter((p) => PLATFORM_META[p].delivery === 'auto' && PLATFORM_META[p].connected).length;
  const manualCount = selected.length - autoCount;
  const Preview = PREVIEWS[tab] || BlogPreview;

  const doPublish = () => {
    const res = selected.map((p) => {
      const m = PLATFORM_META[p];
      if (mode === 'schedule') return { p, state: 'scheduled' };
      if (m.delivery === 'auto' && m.connected) return { p, state: 'publishing' };
      return { p, state: 'manual' };
    });
    setResult(res);
    // simulate auto deploys completing
    setTimeout(() => setResult((r) => r && r.map((x) => x.state === 'publishing' ? { ...x, state: 'done' } : x)), 1600);
  };

  return (
    <div className="pub-scrim" onClick={onClose}>
      <div className="pub" onClick={(e) => e.stopPropagation()}>
        <header className="pub-head">
          <div>
            <h2>{result ? (mode === 'schedule' ? 'Scheduled' : 'Publishing') : (mode === 'schedule' ? 'Schedule' : 'Publish')}</h2>
            <p className="pub-sub">{item?.title || SAMPLE_TITLE}</p>
          </div>
          <button className="x" onClick={onClose}>✕</button>
        </header>

        {!result ? (
          <div className="pub-grid">
            <div className="pub-dests">
              <div className="pub-section-label">Destinations</div>
              {PUBLISHABLE.map((p) => (
                <DestRow key={p} p={p} primary={p === primary}
                  selected={selected.includes(p)} onToggle={() => toggle(p)} />
              ))}
              <div className="pub-section-label sched">When</div>
              <div className="pub-sched">
                <label className={'sched-opt' + (mode !== 'schedule' ? ' on' : '')}>{mode !== 'schedule' ? '◉' : '○'} Publish now</label>
                {mode === 'schedule' && (
                  <input type="datetime-local" value={scheduledAt} onChange={(e) => setScheduledAt(e.target.value)} />
                )}
              </div>
            </div>

            <div className="pub-preview">
              <div className="pub-tabs">
                {selected.map((p) => (
                  <button key={p} className={'pub-tab' + (tab === p ? ' on' : '')} onClick={() => setTab(p)}>
                    <span className="pub-tab-dot" style={{ color: PLATFORM_META[p].color }}>{PLATFORM_META[p].code}</span>
                    {PLATFORM_META[p].label}
                  </button>
                ))}
              </div>
              <div className="pub-canvas">
                <div className="pub-canvas-cap">How it appears on <b>{PLATFORM_META[tab].label}</b> · {PLATFORM_META[tab].delivery === 'auto' && PLATFORM_META[tab].connected ? 'deploys automatically' : PLATFORM_META[tab].delivery === 'manual' ? 'you post this one' : 'connect to auto-deploy'}</div>
                <div className="pub-canvas-inner"><Preview /></div>
              </div>
            </div>
          </div>
        ) : (
          <ResultView result={result} mode={mode} scheduledAt={scheduledAt} />
        )}

        <footer className="pub-foot">
          {!result ? (
            <>
              <div className="pub-summary">
                {selected.length} destination{selected.length !== 1 ? 's' : ''}
                {autoCount > 0 && <span className="ps-auto"> · {autoCount} auto</span>}
                {manualCount > 0 && <span className="ps-manual"> · {manualCount} manual</span>}
              </div>
              <div className="pub-foot-btns">
                <button className="btn-ghost" onClick={onClose}>Cancel</button>
                <button className="btn-primary" disabled={selected.length === 0 || (mode === 'schedule' && !scheduledAt)} onClick={doPublish}>
                  {mode === 'schedule' ? 'Schedule' : `Publish ${selected.length}`} →
                </button>
              </div>
            </>
          ) : (
            <>
              <div className="pub-summary">{mode === 'schedule' ? 'Scheduled across your destinations.' : 'Auto destinations deploy now. Manual ones are ready for you.'}</div>
              <div className="pub-foot-btns">
                <button className="btn-primary" onClick={() => onPublished(item, mode)}>Done</button>
              </div>
            </>
          )}
        </footer>
      </div>
    </div>
  );
}

function ResultView({ result, mode, scheduledAt }) {
  return (
    <div className="pub-result">
      {result.map(({ p, state }) => {
        const m = PLATFORM_META[p];
        return (
          <div key={p} className="res-row">
            <span className="dest-dot" style={{ color: m.color, borderColor: m.color + '66' }}>{m.code}</span>
            <span className="res-name">{m.label}</span>
            <span className="res-right">
              {state === 'publishing' && <span className="res-state pub"><span className="res-spin" /> Publishing…</span>}
              {state === 'done' && <span className="res-state ok">✓ Published <a className="res-link">View ↗</a></span>}
              {state === 'scheduled' && <span className="res-state sched">◴ Scheduled {scheduledAt ? 'for ' + new Date(scheduledAt).toLocaleString() : ''}</span>}
              {state === 'manual' && (
                <span className="res-state manual">
                  Ready to post
                  <button className="res-btn">⧉ Copy text</button>
                  <button className="res-btn">Open {m.label} ↗</button>
                </span>
              )}
            </span>
          </div>
        );
      })}
      {result.some((r) => r.state === 'manual') && (
        <p className="res-note">Manual destinations have no publish API — we've formatted the post for each. Copy it, post it, and check it off.</p>
      )}
    </div>
  );
}

Object.assign(window, { PublishOverlay });
