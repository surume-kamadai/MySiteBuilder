// ============================================================
// renderer.js - HTML生成エンジン（レスポンシブ＆インタラクション拡張版）
// renderer.js - the HTML generation engine (responsive + interaction support).
// ============================================================

import {
    ANIM_CSS, escapeHtml, GOOGLE_FONTS, resolveImageSrc,
    gradientBgDecl, strokeDecl, textExtraCss, combinedShadowDecl, wrapGradText,
} from './css-generator.js';
import { renderSlider, renderArticleGrid, renderAccordion } from './render-components.js';

// シーンデータ（1ページ分: canvas/bgColor/seo/elements）を受け取り、
// 完成したHTML文字列を生成するエンジン。
// Takes one page of scene data (canvas/bgColor/seo/elements) and produces the finished HTML string.
//   - sceneData: { canvas, bgColor, seo, elements, formAction? }
//   - options.mode: 'static'（素のHTML）/ 'blade'（Laravel Blade。@csrf や asset() を使う）
//     options.mode: 'static' (plain HTML) or 'blade' (Laravel Blade, using @csrf / asset()).
//   - options.imageMap: data:URL画像 → 出力パスへの対応表 / map of data:URL images to output paths.
// 生成方針: 各要素を絶対配置(%)で並べ、レスポンシブはCSSクラス + @media、
//           動きのある部品(スライダー/アコーディオン/イベント)は dynamicJs に初期化JSを溜める。
// Approach: absolutely position each element in %, do responsiveness with CSS classes + @media,
//           and queue init JS for interactive parts (slider/accordion/events) in dynamicJs.
export class HtmlRenderer {
    constructor(sceneData, options = {}) {
        this.scene    = sceneData;
        this.mode     = options.mode || 'static';
        this.imageMap = options.imageMap || new Map();

        // CSS分離モード: 指定時は <style> を埋め込まず外部CSSを <link> 参照する。
        // Separate-CSS mode: when set, reference external CSS via <link> instead of inlining <style>.
        // 例: ['css/common.css', 'css/index.css']（Bladeは {{ asset('css/...') }} 形式）
        // e.g. ['css/common.css', 'css/index.css'] (Blade uses the {{ asset('css/...') }} form).
        this.cssHrefs     = options.cssHrefs || null;
        this.extractedCss = null;  // 分離モード時、ページ固有CSSの全文をここへ保持する / holds this page's CSS in separate mode

        // レスポンシブ用CSS と 動的JS を描画中に溜めて、最後に <head>/<script> へ出力する
        // Accumulate responsive CSS and dynamic JS during render, then emit into <head>/<script> at the end.
        this.dynamicCss = [];
        this.dynamicJs  = [];
        this.usedFonts  = new Set();  // 使用された Google Fonts の spec を溜める / specs of the Google Fonts actually used
        this._mobileW = 375;          // スマホ表示の基準幅 / reference width for mobile view
        this._mobileCanvasH = 800;    // スマホ表示の基準高さ / reference height for mobile view
    }

render() {
        const cw = this.scene.canvas?.width  ?? 800;
        const ch = this.scene.canvas?.height ?? 600;

        this._mobileW = this.scene.canvas?.mobileWidth  ?? 375;
        this._mobileCanvasH = this.scene.canvas?.mobileHeight ?? 800;

        const elementsHtml = this.renderElements(this.scene.elements || [], cw, ch, 1);

        // 背景色（ページ個別→サイト共通の解決済み値）。
        // ページ本体(.site-canvas)にも適用しないとエディタと出力で色が一致しない。
        // Background color (already resolved page → site). Must also go on .site-canvas,
        // otherwise the editor and the exported page won't match.
        const bgColor = this.scene.bgColor || '#f1f2f6';

        this.dynamicCss.push(
            `.site-canvas { position: relative; width: 100%; max-width: ${cw}px; aspect-ratio: ${cw} / ${ch}; background-color: ${bgColor}; box-shadow: 0 0 30px rgba(0,0,0,0.1); overflow: hidden; margin: 0 auto; transition: all 0.3s ease; }`
        );
        this.dynamicCss.push(
            `@media (max-width: 768px) { .site-canvas { max-width: 100%; aspect-ratio: ${this._mobileW} / ${this._mobileCanvasH}; } }`
        );

        const cssString = this.dynamicCss.join('\n    ');

        let jsString = '';
        if (this.dynamicJs.length > 0) {
            jsString = `\n<script>\ndocument.addEventListener("DOMContentLoaded", function() {\n    ${this.dynamicJs.join('\n    ')}\n});\n</script>\n`;
        }

        // ▼▼ SEO メタタグの構築 / Build the SEO meta tags ▼▼
        const seo   = this.scene.seo || {};
        const lang  = escapeHtml(seo.lang || 'ja');
        const title = escapeHtml(seo.title || 'ページ');

        let html  = `<!DOCTYPE html>\n<html lang="${lang}">\n<head>\n`;
        html += '    <meta charset="UTF-8">\n';
        html += '    <meta name="viewport" content="width=device-width, initial-scale=1.0">\n';
        html += `    <title>${title}</title>\n`;
        if (seo.description) {
            html += `    <meta name="description" content="${escapeHtml(seo.description)}">\n`;
        }
        // OGP / Twitter カード / OGP / Twitter card
        html += '    <meta property="og:type" content="website">\n';
        html += `    <meta property="og:title" content="${title}">\n`;
        if (seo.description) html += `    <meta property="og:description" content="${escapeHtml(seo.description)}">\n`;
        if (seo.ogImage)     html += `    <meta property="og:image" content="${escapeHtml(seo.ogImage)}">\n`;
        if (seo.siteName)    html += `    <meta property="og:site_name" content="${escapeHtml(seo.siteName)}">\n`;
        html += `    <meta name="twitter:card" content="${seo.ogImage ? 'summary_large_image' : 'summary'}">\n`;
        // ▲▲ SEO メタタグ構築ここまで / End of SEO meta tags ▲▲

        // ▼ 使用された Google Fonts の読み込み（該当フォントを使った時だけ）
        // ▼ Load the Google Fonts that were actually used (only when any were used).
        if (this.usedFonts.size > 0) {
            html += '    <link rel="preconnect" href="https://fonts.googleapis.com">\n';
            html += '    <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>\n';
            const fams = [...this.usedFonts].map(s => 'family=' + s).join('&');
            html += `    <link href="https://fonts.googleapis.com/css2?${fams}&display=swap" rel="stylesheet">\n`;
        }

        if (this.cssHrefs) {
            // 分離モード: 外部CSSを <link> 参照する。ページ固有CSSは extractedCss に保持し、
            // 共通のアニメーションCSS(ANIM_CSS)は exporter が common.css として出力する。
            // Separate mode: reference external CSS via <link>. The page CSS is kept in extractedCss,
            // and the shared animation CSS (ANIM_CSS) is written by the exporter as common.css.
            // href は Blade の {{ asset('...') }} を壊さないよう escapeHtml せず、
            // 属性を破壊しうる二重引用符だけを無害化する。
            // Don't escapeHtml the href (it would break Blade's {{ asset('...') }}); only neutralize
            // the double quote that could break out of the attribute.
            for (const href of this.cssHrefs) {
                html += `    <link rel="stylesheet" href="${String(href).replace(/"/g, '&quot;')}">\n`;
            }
            this.extractedCss = cssString;
        } else {
            html += '    <style>\n' + ANIM_CSS + '\n    </style>\n';
            html += '    <style id="dynamic-styles">\n    ' + cssString + '\n    </style>\n';
        }

        // ▼▼ 追加: スライダーがあればSwiperのCSSを読み込む / If a slider exists, load Swiper's CSS ▼▼
        if (elementsHtml.includes('class="swiper')) {
            html += '    <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/swiper@11/swiper-bundle.min.css" />\n';
            // Swiperのデフォルトの矢印/ドットの色を白や任意の色に変えたい場合のスタイル補正
            // Override Swiper's default arrow/dot color (here: white).
            html += '    <style>:root { --swiper-theme-color: #ffffff; }</style>\n';
        }

        html += `</head>\n<body style="margin: 0; background-color: ${bgColor};">\n\n`;

        // ▼▼ フォーム: 送信ボタンがあればページ全体を <form> でラップ ▼▼
        // ▼▼ Form: if there's a submit button, wrap the whole page in a <form> ▼▼
        const submit = this._findSubmitButton(this.scene.elements || []);
        let formOpen = '', formClose = '', afterCanvas = '';
        if (submit) {
            const userAction = (submit.route && submit.route !== '#') ? submit.route : '';
            const successMsg = (submit.successMessage ?? '送信ありがとうございました。');
            if (this.mode === 'blade') {
                // Laravel: exporter が決めた action を優先、無ければ /contact
                // Laravel: prefer the action decided by the exporter, else /contact.
                const action = escapeHtml(this.scene.formAction || userAction || '/contact');
                formOpen  = `<form action="${action}" method="POST">\n@csrf`;
                formClose = `</form>`;
                // 送信後: controller の back()->with('success', ...) を受けて表示
                // After submit: shown via the controller's back()->with('success', ...).
                if (successMsg) afterCanvas = this._successOverlay(escapeHtml(successMsg), true);
            } else {
                // 静的: Googleフォーム/Formspree 等のエンドポイントへ送信
                // Static: submit to an endpoint like Google Forms / Formspree.
                const action = escapeHtml(userAction || '#');
                const method = (submit.method || 'POST').toUpperCase() === 'GET' ? 'GET' : 'POST';
                if (successMsg) {
                    // 完了メッセージあり: 隠しiframeへ送信してページ遷移させず、メッセージを表示
                    // With a done message: submit to a hidden iframe so the page doesn't navigate, then show the message.
                    formOpen  = `<form action="${action}" method="${method}" target="ksb_form_target" onsubmit="setTimeout(function(){var s=document.getElementById('ksb-form-success');if(s)s.style.display='flex';},400);">`;
                    formClose = `</form>`;
                    afterCanvas = `<iframe name="ksb_form_target" style="display:none"></iframe>\n` + this._successOverlay(escapeHtml(successMsg), false);
                } else {
                    // 完了メッセージなし: 送信先ページへそのまま遷移
                    // No done message: just navigate to the target page.
                    formOpen  = `<form action="${action}" method="${method}">`;
                    formClose = `</form>`;
                }
            }
        }

        html += `<div class="site-canvas">\n`;
        if (formOpen)  html += formOpen + '\n';
        html += elementsHtml;
        if (formClose) html += formClose + '\n';
        html += '\n</div>\n';
        if (afterCanvas) html += afterCanvas + '\n';
        
        // ▼▼ 追加: スライダーがあればSwiperのJS本体を読み込む / If a slider exists, load Swiper's JS ▼▼
        if (elementsHtml.includes('class="swiper')) {
            html += '<script src="https://cdn.jsdelivr.net/npm/swiper@11/swiper-bundle.min.js"></script>\n';
        }
        
        html += jsString;
        html += '\n</body>\n</html>';

        return html;
    }

    // 分離モード時、ページ固有CSS（.site-canvas と .el-xxx ルール群）の全文を返す。
    // In separate mode, return this page's CSS (the .site-canvas and .el-xxx rules).
    // render() を呼んだ後に使う。従来（埋め込み）モードでは null。
    // Call after render(). Returns null in the classic (inlined) mode.
    getExtractedCss() { return this.extractedCss; }

    // 要素ツリーを再帰的にたどり、最初の「送信ボタン」(role==='submit')の
    // プロパティを返す。無ければ null。
    // Recurse the element tree and return the first submit button's (role==='submit') props, or null.
    _findSubmitButton(elements) {
        for (const el of (elements || [])) {
            const p = el.properties || {};
            if (p.visible === false) continue;
            if (el.type === 'Button' && p.role === 'submit') return p;
            if (Array.isArray(el.children)) {
                const found = this._findSubmitButton(el.children);
                if (found) return found;
            }
        }
        return null;
    }

    // 送信完了メッセージの中央オーバーレイ。
    // Centered overlay for the "submission complete" message.
    // 静的は display:none で出力し JS で表示。Blade は session('success') で表示。
    // Static: emitted as display:none and shown via JS. Blade: shown via session('success').
    _successOverlay(msg, isBlade) {
        const inner = `<div style="background:#fff; padding:24px 32px; border-radius:10px; font-size:16px; color:#222; box-shadow:0 10px 40px rgba(0,0,0,0.3); max-width:80%; text-align:center;">${msg}<br><button type="button" onclick="document.getElementById('ksb-form-success').style.display='none'" style="margin-top:16px; padding:8px 20px; border:none; border-radius:6px; background:#007acc; color:#fff; cursor:pointer;">OK</button></div>`;
        const style = `position:fixed; inset:0; align-items:center; justify-content:center; background:rgba(0,0,0,0.5); z-index:9999;`;
        if (isBlade) {
            return `@if(session('success'))\n<div id="ksb-form-success" style="display:flex; ${style}">${inner}</div>\n@endif`;
        }
        return `<div id="ksb-form-success" style="display:none; ${style}">${inner}</div>`;
    }

    // 要素配列を再帰的にHTML文字列へ変換する中核メソッド。
    // Core method that recursively turns an element array into an HTML string.
    // - parentW/parentH: 親の基準サイズ（%座標の分母。トップレベルはキャンバスサイズ）
    //   parentW/parentH: the parent's reference size (denominator for % coords; canvas size at top level).
    // - depth: ネスト深さ（インデント用） / nesting depth (used for indentation).
    // 各要素について「レスポンシブ用CSS(.el-id)」「イベントJS」「本体HTML」を生成する。
    // For each element, produces its responsive CSS (.el-id), event JS, and body HTML.
    renderElements(elements, parentW, parentH, depth) {
        let out = '';
        const indent = '    '.repeat(depth);

        for (const el of (elements || [])) {
            const id    = el.id;
            const type  = el.type;
            const props = el.properties || {};

            if (props.visible === false) continue;

            // ▼▼ レスポンシブ座標の抽出とCSS構築（方針A: transformを正とする）▼▼
            // ▼▼ Extract responsive coords and build CSS (policy A: transform is the source of truth) ▼▼
            // PC配置は常に transform（正しい左上座標）を使う
            // PC layout always uses transform (the correct top-left coordinate).
            const tf = el.transform || { x: 0, y: 0, width: 100, height: 50 };
            const pcW = tf.width;
            const pcH = tf.height;

            // PC用パーセント座標計算 / Compute the PC percentage coordinates.
            const leftPc = parentW > 0 ? (tf.x / parentW) * 100 : 0;
            const topPc  = parentH > 0 ? (tf.y / parentH) * 100 : 0;
            const wPc    = parentW > 0 ? (pcW / parentW) * 100 : 0;
            const hPc    = parentH > 0 ? (pcH / parentH) * 100 : 0;
            const fontPc = props.fontsize || 16;

            // スマホ用：mobileEdited が true の要素だけ mobile レイアウトで上書き。
            // それ以外は PC のパーセント値をそのまま使う（相対配置を維持）。
            // Mobile: only elements with mobileEdited=true are overridden by the mobile layout;
            // the rest reuse the PC percentages (keeping their relative placement).
            let leftMo = leftPc, topMo = topPc, wMo = wPc, hMo = hPc, fontMo = fontPc;
            if (props.mobileEdited && props.layouts?.mobile) {
                const lMo = props.layouts.mobile;
                const canvasW = this.scene.canvas?.width  || 800;
                const canvasH = this.scene.canvas?.height || 600;
                const parentMoW = (parentW === canvasW) ? this._mobileW : parentW;
                const parentMoH = (parentH === canvasH) ? this._mobileCanvasH : parentH;
                leftMo = parentMoW > 0 ? (lMo.x / parentMoW) * 100 : leftPc;
                topMo  = parentMoH > 0 ? (lMo.y / parentMoH) * 100 : topPc;
                wMo    = parentMoW > 0 ? (lMo.w / parentMoW) * 100 : wPc;
                hMo    = parentMoH > 0 ? (lMo.h / parentMoH) * 100 : hPc;
                fontMo = lMo.fontsize || fontPc;
            }

            // IDごとのCSSクラスを生成 / Make a per-ID CSS class.
            const className = `el-${id}`;

            // PC用スタイル (デフォルト) / PC style (the default).
            // ArticleGrid は中身に応じて高さを伸ばす（固定高さだとカードが潰れる）
            // ArticleGrid grows to fit its content (a fixed height would squash the cards).
            const heightRule = (type === 'ArticleGrid' || type === 'Accordion')
                ? `height: auto; min-height: ${hPc}%`
                : `height: ${hPc}%`;
            // 不透明度（0〜1）。既定1のときは出力しない（アニメーションのopacityと競合させないため）。
            // Opacity (0–1). Omitted when it's the default 1, to avoid clashing with animation opacity.
            const opacity = (typeof props.opacity === 'number' && Number.isFinite(props.opacity))
                ? Math.min(1, Math.max(0, props.opacity)) : 1;
            const opacityRule = opacity !== 1 ? ` opacity: ${opacity};` : '';
            let cssRule = `.${className} { left: ${leftPc}%; top: ${topPc}%; width: ${wPc}%; ${heightRule}; font-size: ${fontPc}px;${opacityRule} transition: all 0.3s ease; }`;

            // スマホ: font-size を vw 基準にして、画面幅に応じて文字も拡縮させる
            // Mobile: base font-size on vw so text scales with the screen width.
            const fontMoVw = (fontMo / this._mobileW * 100).toFixed(2);
            const heightRuleMo = (type === 'ArticleGrid' || type === 'Accordion')
                ? `height: auto`
                : `height: ${hMo}%`;
            cssRule += `\n    @media (max-width: 768px) { .${className} { left: ${leftMo}%; top: ${topMo}%; width: ${wMo}%; ${heightRuleMo}; font-size: ${fontMoVw}vw; } }`;
            
            this.dynamicCss.push(cssRule);
            // ▲▲ レスポンシブCSS構築ここまで / End of responsive CSS ▲▲

            // ▼▼ イベント(JS)の構築 / Build the event handlers (JS) ▼▼
            if (props.events && props.events.length > 0) {
                props.events.forEach(ev => {
                    const eventName = ev.trigger === 'hover' ? 'mouseenter' : 'click';
                    let actionJs = '';
                    
                    if (ev.action === 'alert') {
                        const safeMsg = (ev.target || '').replace(/"/g, '\\"');
                        actionJs = `alert("${safeMsg}");`;
                    } else if (ev.target) { // ID指定のターゲット操作 / operate on the target element by id
                        actionJs = `
            var t = document.getElementById("${ev.target}");
            if (t) {
                ${ev.action === 'show' ? 't.style.display = "block";' : ''}
                ${ev.action === 'hide' ? 't.style.display = "none";' : ''}
                ${ev.action === 'toggle' ? 't.style.display = (t.style.display === "none" ? "block" : "none");' : ''}
            }`;
                    }

                    if (actionJs) {
                        this.dynamicJs.push(`
    var el_${id} = document.getElementById("${id}");
    if (el_${id}) {
        el_${id}.addEventListener("${eventName}", function(e) {
            e.preventDefault();
            ${actionJs}
        });
    }`);
                    }
                });
            }
            // ▲▲ イベント(JS)構築ここまで / End of event handlers ▲▲

            // 共通プロパティの展開 / Expand the common properties.
            const text     = escapeHtml(props.text ?? '');
            const name     = escapeHtml(props.name ?? 'Unnamed');
            const bgcolor  = escapeHtml(props.bgcolor ?? 'transparent');
            const bgFill   = gradientBgDecl(props, bgcolor);
            const cornerR  = Math.max(0, parseInt(props.cornerRadius) || 0);
            const radiusCss = cornerR ? ` border-radius: ${cornerR}px;` : '';
            const strokeCss = strokeDecl(props, type);
            const color    = escapeHtml(props.color ?? 'inherit');
            const align    = escapeHtml(props.align || (type === 'Button' ? 'center' : 'left'));
            const fontfam  = escapeHtml(props.fontfamily || 'sans-serif');
            // 使用中の Google Font を検出（出力headに<link>する）
            // Detect which Google Fonts are in use (they get <link>ed into the exported head).
            const rawFam = props.fontfamily || '';
            GOOGLE_FONTS.forEach(f => { if (rawFam.includes(f.family)) this.usedFonts.add(f.spec); });

            let animClass = className; // レスポンシブ用クラスを割り当て / assign the responsive class
            if (props.animation && props.animation !== 'none') {
                animClass += ' anim-' + String(props.animation).toLowerCase();
            }

            // ドロップシャドウ/プリセット＋光彩＋内側シャドウ＋ベベルを合成した影スタイル
            // Shadow style combining drop shadow/preset + glow + inner shadow + bevel.
            const shadowStyle = combinedShadowDecl(props, type);

            // width等を除いたベーススタイル / Base style (without width/height etc.).
            let baseStyle = `position: absolute; box-sizing: border-box;`;
            // Group / ArticleGrid / Accordion は自前でレイアウトを組むので baseStyle に背景を付けない
            // Group / ArticleGrid / Accordion lay themselves out, so don't put a background on baseStyle.
            if (type !== 'Group' && type !== 'ArticleGrid' && type !== 'Accordion' && type !== 'Triangle') {
                baseStyle += ` ${bgFill} color: ${color}; text-align: ${align}; font-family: ${fontfam};`;
                if (type !== 'Button' && type !== 'Image') baseStyle += ` ${shadowStyle}`;
            }
            baseStyle += strokeCss;  // 境界線（テキストは文字縁取り、他は border）/ stroke (text-stroke for text, border otherwise)

            out += `${indent}\n`;

            switch (type) {
                case 'Group':
                    out += this.renderGroup(id, animClass, baseStyle, bgcolor, el, pcW, pcH, depth, indent);
                    break;
                case 'Button':
                    out += this.renderButton(id, animClass, baseStyle, bgcolor, color, text, props, shadowStyle, indent);
                    break;
                case 'TextInput':
                    out += this.renderTextInput(id, animClass, baseStyle, text, props, indent);
                    break;
                case 'Label':
                    out += this.renderLabel(id, animClass, baseStyle, color, text, props, shadowStyle, indent);
                    break;
                case 'Rect':
                    out += `${indent}<div id="${id}" class="${animClass}" style="${baseStyle}${radiusCss}"></div>\n`;
                    break;
                case 'Circle':
                    out += `${indent}<div id="${id}" class="${animClass}" style="${baseStyle} border-radius: 50%;"></div>\n`;
                    break;
                case 'Warp': {
                    const pts = props.warpPoints || [];
                    if (pts.length >= 3) {
                        const xs = pts.map(p => p.x), ys = pts.map(p => p.y);
                        const minX = Math.min(...xs), minY = Math.min(...ys);
                        const bw = Math.max(...xs) - minX || 1;
                        const bh = Math.max(...ys) - minY || 1;
                        const poly = pts.map(p =>
                            `${(((p.x - minX) / bw) * 100).toFixed(2)}% ${(((p.y - minY) / bh) * 100).toFixed(2)}%`
                        ).join(', ');
                        const warpStyle = `position: absolute; left: ${leftPc}%; top: ${topPc}%; width: ${wPc}%; height: ${hPc}%; background-color: ${bgcolor}; clip-path: polygon(${poly}); ${shadowStyle}`;
                        out += `${indent}<div id="${id}" class="${animClass}" style="${warpStyle}"></div>\n`;
                    } else {
                        out += `${indent}<div id="${id}" class="${animClass}" style="${baseStyle}"></div>\n`;
                    }
                    break;
                }
                case 'Triangle':
                    const triStyle = `position: absolute; width: 100%; height: 100%; box-sizing: border-box; clip-path: polygon(50% 0%, 100% 100%, 0% 100%); ${bgFill}`;
                    out += `${indent}<div id="${id}" class="${animClass}" style="${baseStyle}"><div style="${triStyle}"></div></div>\n`;
                    break;
                case 'Image':
                    out += this.renderImage(id, animClass, baseStyle, props, name, shadowStyle, indent);
                    break;
                case 'Slider':
                out += renderSlider(this, id, animClass, baseStyle, props, indent);
                break;
                case 'ArticleGrid':
                out += renderArticleGrid(this, id, animClass, baseStyle, props, indent);
                break;
                case 'Accordion':
                out += renderAccordion(this, id, animClass, baseStyle, props, indent);
                break;
            }
        }
        return out;
    }

    // グループ（入れ子コンテナ）。子要素を自身のサイズを基準に再帰描画する。
    // Group (nested container). Recursively renders children relative to its own size.
    renderGroup(id, animClass, baseStyle, bgcolor, el, width, height, depth, indent) {
        let out = `${indent}<div id="${id}" class="${animClass}" style="${baseStyle} background-color: ${bgcolor};">\n`;
        if (Array.isArray(el.children)) {
            out += this.renderElements(el.children, width, height, depth + 1);
        }
        out += `${indent}</div>\n`;
        return out;
    }

    // ボタン。role==='submit' は <form> 送信ボタン、それ以外は <a> リンクとして出力。
    // Button. role==='submit' becomes a <form> submit button; otherwise it's an <a> link.
    // 背景画像があれば background-image、文字揃えは props.align を反映。
    // Uses background-image when a bgimage is set, and applies props.align for text alignment.
    renderButton(id, animClass, baseStyle, bgcolor, color, text, props, shadowStyle, indent) {
        let bgStyle = gradientBgDecl(props, bgcolor);
        if (props.bgimage) {
            const src = escapeHtml(resolveImageSrc(props.bgimage, this.imageMap));
            bgStyle = `background-image: url('${src}'); background-size: cover; background-position: center;`;
        }

        // <button> はブラウザ既定で text-align:center になるため、揃え設定を明示する
        // Browsers default <button> to text-align:center, so state the alignment explicitly.
        const align = escapeHtml(props.align || 'center');
        const btnR = Math.max(0, parseInt(props.cornerRadius ?? 8) || 0);
        const btnStyle = `width: 100%; height: 100%; box-sizing: border-box; ${bgStyle} color: ${color}; font-size: inherit; border: none; border-radius: ${btnR}px; cursor: pointer; font-weight: ${props.fontWeight || 'bold'}; text-align: ${align}; ${shadowStyle}${strokeDecl(props, 'Button')}${textExtraCss(props)}`;
        const formStyle = `margin: 0; position: absolute; width: 100%; height: 100%;`;
        // グラデ文字はボタン背景と衝突するため、文字を span で包んで適用する
        // Gradient text clashes with the button background, so wrap the text in a span.
        const btnText = wrapGradText(text, props);

        // 送信ボタン: ページ全体を包む <form>（render側で出力）が送信を担うので
        // ここでは type="submit" のボタンを置くだけ。リンクや内部フォームは付けない。
        // Submit button: the page-wrapping <form> (emitted in render) handles submission, so
        // here we just place a type="submit" button — no link, no inner form.
        if (props.role === 'submit') {
            let out = `${indent}<div id="${id}" class="${animClass}" style="${baseStyle} background:none;">\n`;
            out += `${indent}    <button type="submit" style="${btnStyle} ${formStyle}">${btnText}</button>\n`;
            out += `${indent}</div>\n`;
            return out;
        }

        // 通常ボタン: 静的・Blade とも <a> リンクとして出力する
        // Normal button: emitted as an <a> link in both static and Blade modes.
        const url = escapeHtml(props.route && props.route !== '#' ? props.route : '#');
        let out = `${indent}<div id="${id}" class="${animClass}" style="${baseStyle} background:none;">\n`;
        out += `${indent}    <a href="${url}" style="${formStyle} display:block; text-decoration:none;">\n`;
        out += `${indent}        <button type="button" style="${btnStyle}">${btnText}</button>\n`;
        out += `${indent}    </a>\n`;
        out += `${indent}</div>\n`;
        return out;
    }

    // 入力欄。inputName(name属性)・inputType(text/email/tel/number/textarea)・required を反映。
    // Text input. Reflects inputName (name attr), inputType (text/email/tel/number/textarea), and required.
    renderTextInput(id, animClass, baseStyle, text, props, indent) {
        // フォーム項目として name / 種類 / 必須 を反映する / Apply the form field's name / type / required.
        const name      = escapeHtml(props.inputName || '');
        const nameAttr  = name ? ` name="${name}"` : '';
        const required  = props.required ? ' required' : '';
        const ph        = escapeHtml(text || '');
        // 入力タイプは想定値のみ許可（安全側） / Allow only known input types (safe by default).
        const allowed   = ['text', 'email', 'tel', 'number'];
        const rawType   = props.inputType || 'text';

        if (rawType === 'textarea') {
            const style = baseStyle + ' padding: 10px; border: 1px solid #ccc; border-radius: 4px; background-color: #fff; width: 100%; height: 100%; resize: none; font-family: inherit;';
            return `${indent}<div id="${id}" class="${animClass}" style="position:absolute;"><textarea${nameAttr} placeholder="${ph}"${required} style="${style}"></textarea></div>\n`;
        }

        const itype = allowed.includes(rawType) ? rawType : 'text';
        const style = baseStyle + ' padding: 10px; border: 1px solid #ccc; border-radius: 4px; background-color: #fff; width: 100%; height: 100%;';
        return `${indent}<div id="${id}" class="${animClass}" style="position:absolute;"><input type="${itype}"${nameAttr} placeholder="${ph}"${required} style="${style}"></div>\n`;
    }

    // テキスト（見出し・本文）。単純な div として出力する。
    // Text (heading / body). Emitted as a plain div.
    renderLabel(id, animClass, baseStyle, color, text, props, shadowStyle, indent) {
        const style = `${baseStyle} display: block; overflow: hidden; ${shadowStyle} font-weight: ${props.fontWeight || 'normal'};${textExtraCss(props)}`;
        // グラデ文字は背景クリップと衝突するため、文字を span で包んで適用する
        // Gradient text clashes with background-clip, so wrap the text in a span.
        return `${indent}<div id="${id}" class="${animClass}" style="${style}">${wrapGradText(text, props)}</div>\n`;
    }

    // 画像。object-fit:contain で「拡大・切り取りせず全体表示」（縦横比維持）。
    // Image. object-fit:contain shows the whole image without cropping (keeps aspect ratio).
    // route があればリンク化、画像は imageMap で data:URL → 出力パス
    // （静的は images/...、Bladeは {{ asset(...) }}）へ解決。
    // If a route is set it becomes a link; images resolve data:URL → output path via imageMap
    // (images/... for static, {{ asset(...) }} for Blade).
    renderImage(id, animClass, baseStyle, props, name, shadowStyle, indent) {
        const src = escapeHtml(resolveImageSrc(props.text, this.imageMap));
        const route = props.route ?? '#';
        const hasLink = route !== '#' && route !== '' && route !== 'none';
        const r = Math.max(0, parseInt(props.cornerRadius) || 0);
        const rc = r ? ` border-radius: ${r}px;` : '';
        const imgStyle = `width: 100%; height: 100%; object-fit: contain; display: block; ${shadowStyle}${rc}`;
        const g = props.gradient;
        // グラデーション on の画像は、画像の上に gradient を乗算で重ねるオーバーレイを置く
        // For a gradient-on image, place an overlay that multiply-blends the gradient over it.
        const overlay = (g && g.on)
            ? `<div style="position:absolute; inset:0; ${gradientBgDecl(props, '')} mix-blend-mode:multiply; pointer-events:none;"></div>`
            : '';

        if (hasLink) {
            const url = escapeHtml(route);
            let inner = `<img src="${src}" alt="${name}" style="${imgStyle}">`;
            inner = `<a href="${url}" style="display:block; width:100%; height:100%;">${inner}</a>`;
            return `${indent}<div id="${id}" class="${animClass}" style="${baseStyle} background:none; overflow:hidden;">${inner}${overlay}</div>\n`;
        }

        if (overlay) {
            return `${indent}<div id="${id}" class="${animClass}" style="${baseStyle} overflow:hidden;"><img src="${src}" alt="${name}" style="${imgStyle}">${overlay}</div>\n`;
        }

        return `${indent}<img id="${id}" src="${src}" alt="${name}" class="${animClass}" style="${baseStyle} ${imgStyle}">\n`;
    }
    // 画像スライダー。Swiper.js のマークアップを生成し、初期化JSを dynamicJs に積む。
    // slides[]（画像/タイトル/本文/リンク）と各種オプション（効果/速度/自動再生等）に対応。
}