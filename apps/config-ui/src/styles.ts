import { ROOT_ID, STYLE_ID } from './constants';

export function ensureStyles(): void {
  if (document.getElementById(STYLE_ID)) return;

  const style = document.createElement('style');
  style.id = STYLE_ID;
  const rootSel = `#${ROOT_ID}`;

  style.textContent = `
${rootSel}{max-width:1040px;color:inherit}
${rootSel} *{box-sizing:border-box}
${rootSel} .jc-wrap{display:grid;gap:1rem}
${rootSel} .jc-card{border:1px solid rgba(255,255,255,.08);border-radius:16px;background:linear-gradient(180deg,rgba(255,255,255,.02),rgba(255,255,255,.01));box-shadow:0 12px 28px rgba(0,0,0,.15)}
${rootSel} .jc-cardBody{padding:1rem}
${rootSel} .jc-hero{background:radial-gradient(circle at 100% 0, rgba(67,217,189,.08), transparent 42%)}
${rootSel} .jc-title{margin:0 0 .4rem;font-size:1.2rem}
${rootSel} .jc-sub{margin:0;color:rgba(255,255,255,.72);line-height:1.35}
${rootSel} .jc-grid3{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:.75rem;margin-top:.9rem}
${rootSel} .jc-stat{padding:.75rem;border-radius:12px;border:1px solid rgba(255,255,255,.08);background:rgba(255,255,255,.02)}
${rootSel} .jc-statLabel{display:block;font-size:.72rem;letter-spacing:.08em;text-transform:uppercase;opacity:.75;margin-bottom:.2rem}
${rootSel} .jc-statValue{display:block;font-weight:700;line-height:1.25}
${rootSel} .jc-pill{display:inline-block;padding:.18rem .5rem;border-radius:999px;font-size:.75rem;font-weight:700;border:1px solid rgba(255,255,255,.14);background:rgba(255,255,255,.03)}
${rootSel} .jc-pill.ok{border-color:rgba(67,217,189,.35);background:rgba(67,217,189,.12);color:#a8f3e6}
${rootSel} .jc-pill.off{border-color:rgba(255,109,109,.35);background:rgba(255,109,109,.10);color:#ffbdbd}
${rootSel} .jc-pill.warn{border-color:rgba(255,177,74,.35);background:rgba(255,177,74,.1);color:#ffdb9f}
${rootSel} .jc-note{margin-top:.8rem;padding:.75rem .85rem;border-radius:12px;border:1px solid rgba(255,255,255,.08);line-height:1.35}
${rootSel} .jc-note.ok{border-color:rgba(67,217,189,.2);background:rgba(67,217,189,.06)}
${rootSel} .jc-note.warn{border-color:rgba(255,177,74,.2);background:rgba(255,177,74,.06)}
${rootSel} .jc-note.error{border-color:rgba(255,109,109,.2);background:rgba(255,109,109,.06)}
${rootSel} .jc-form{display:grid;gap:.85rem}
${rootSel} .jc-stack{display:grid;gap:.85rem}
${rootSel} .jc-row2{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:.85rem}
${rootSel} .jc-field{padding:.8rem;border-radius:12px;border:1px solid rgba(255,255,255,.08);background:rgba(255,255,255,.02)}
${rootSel} .jc-field label{display:block;font-weight:600;margin-bottom:.35rem}
${rootSel} .jc-help{margin-top:.35rem;color:rgba(255,255,255,.68);font-size:.85rem;line-height:1.3}
${rootSel} .fieldDescription{margin-top:.2rem;color:rgba(255,255,255,.68);font-size:.85rem;line-height:1.3}
${rootSel} .jc-field input[type=number], ${rootSel} .jc-field input[type=text], ${rootSel} .jc-field select{
  width:100%;
  padding:.55rem .65rem;
  border-radius:10px;
  border:1px solid rgba(255,255,255,.14);
  background:rgba(0,0,0,.16);
  color:inherit;
}
${rootSel} .jc-checkGrid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:.85rem}
${rootSel} .jc-check{padding:.8rem;border-radius:12px;border:1px solid rgba(255,255,255,.08);background:rgba(255,255,255,.02)}
${rootSel} .jc-check .checkboxContainer{margin:0}
${rootSel} .jc-check input[type=checkbox]{margin-right:.45rem;vertical-align:middle}
${rootSel} .jc-check label{font-weight:600}
${rootSel} .jc-divider{height:1px;border:0;margin:.2rem 0;background:linear-gradient(90deg,transparent,rgba(255,255,255,.14),transparent)}
${rootSel} .jc-actions{display:flex;align-items:center;justify-content:space-between;gap:.75rem;flex-wrap:wrap}
${rootSel} .jc-meta{font-size:.85rem;color:rgba(255,255,255,.75)}
${rootSel} .jc-actions button{
  appearance:none;
  border:1px solid rgba(67,217,189,.35);
  border-radius:999px;
  padding:.6rem 1rem;
  background:linear-gradient(180deg, rgba(67,217,189,.16), rgba(67,217,189,.08));
  color:#d6fff7;
  font-weight:700;
  cursor:pointer;
}
${rootSel} .jc-actions button:hover:not(:disabled){filter:brightness(1.05)}
${rootSel} .jc-actions button:disabled{opacity:.55;cursor:default}
${rootSel} .jc-hidden{display:none !important}
@media (max-width:900px){${rootSel} .jc-grid3,${rootSel} .jc-row2,${rootSel} .jc-checkGrid{grid-template-columns:1fr}}
`;

  (document.head || document.documentElement).appendChild(style);
}
