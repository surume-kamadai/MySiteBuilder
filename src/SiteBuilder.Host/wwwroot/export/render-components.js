// ============================================================
// render-components.js - 動的部品（スライダー/記事グリッド/アコーディオン）のHTML生成
// render-components.js - HTML generation for dynamic parts (slider / article grid / accordion).
// HtmlRenderer から分離。第1引数 r に renderer インスタンスを受け取り、
// r.dynamicJs / r.dynamicCss / r.imageMap を通じて初期化JS・CSS・画像解決を行う。
// Split out of HtmlRenderer. Takes the renderer instance as the first arg (r) and uses
// r.dynamicJs / r.dynamicCss / r.imageMap for init JS, CSS, and image resolution.
// ============================================================
import { escapeHtml, resolveImageSrc } from './css-generator.js';

// 画像スライダー（Swiper.js）。slides[] からマークアップを作り初期化JSを積む。
// Image slider (Swiper.js). Builds markup from slides[] and pushes its init JS.
export function renderSlider(r, id, animClass, baseStyle, props, indent) {
        // 新スキーマ slides[] 優先、無ければ旧 text(カンマ区切り画像URL)から変換
        // Prefer the new slides[] schema; otherwise convert from legacy text (comma-separated image URLs).
        let slides = props.slider?.slides;
        if (!Array.isArray(slides) || slides.length === 0) {
            const legacy = (props.text || '').split(',').map(s => s.trim()).filter(Boolean);
            slides = legacy.map(url => ({ image: url, title: '', text: '', linkType: 'none', link: '' }));
        }

        if (slides.length === 0) {
            return `${indent}<div id="${id}" style="${baseStyle} background:#333; color:#fff; display:flex; align-items:center; justify-content:center;">スライドが設定されていません</div>\n`;
        }

        const s = props.slider || {};
        const effect       = s.effect ?? 'slide';
        const speed        = s.speed ?? 600;
        const autoplay     = s.autoplay ?? true;
        const delay        = s.delay ?? 3000;
        const loop         = s.loop ?? true;
        const pagination   = s.pagination ?? true;
        const navigation   = s.navigation ?? true;
        const slidesPerView = s.slidesPerView ?? 1;

        const useGrid  = effect === 'grid';
        const useCards = effect === 'cards';
        const useCover = effect === 'coverflow';
        const useCube  = effect === 'cube';
        const useFade  = effect === 'fade';

        // Swiperのエフェクト名（grid は slide扱い + slidesPerView複数）
        // Swiper effect name (grid is treated as 'slide' with multiple slidesPerView).
        const swiperEffect = useGrid ? 'slide' : effect;

        let out = `${indent}<div id="${id}" class="swiper ${animClass}" style="${baseStyle} border-radius: 5px; overflow:hidden;">\n`;
        out += `${indent}    <div class="swiper-wrapper">\n`;

        slides.forEach((sl) => {
            const img = sl.image ? escapeHtml(resolveImageSrc(sl.image, r.imageMap)) : '';
            const title = escapeHtml(sl.title || '');
            const text  = escapeHtml(sl.text  || '');
            const hasOverlay = !!(sl.title || sl.text);

            // リンク種別 / Link kind (external URL / internal page / none)
            let openTag = '', closeTag = '';
            if (sl.linkType === 'url' && sl.link) {
                const url = escapeHtml(sl.link);
                openTag = `<a href="${url}" target="_blank" rel="noopener noreferrer" style="display:block; width:100%; height:100%; text-decoration:none; color:inherit;">`;
                closeTag = `</a>`;
            } else if (sl.linkType === 'page' && sl.link) {
                const url = escapeHtml(sl.link);
                openTag = `<a href="${url}" style="display:block; width:100%; height:100%; text-decoration:none; color:inherit;">`;
                closeTag = `</a>`;
            }

            out += `${indent}        <div class="swiper-slide" style="position:relative; background:#222;">\n`;
            out += `${indent}            ${openTag}\n`;
            if (img) {
                out += `${indent}                <img src="${img}" style="width:100%; height:100%; object-fit:cover; display:block;">\n`;
            }
            if (hasOverlay) {
                out += `${indent}                <div style="position:absolute; left:0; right:0; bottom:0; padding:16px 20px; background:linear-gradient(transparent, rgba(0,0,0,0.7)); color:#fff;">\n`;
                if (title) out += `${indent}                    <div style="font-size:18px; font-weight:bold; margin-bottom:4px;">${title}</div>\n`;
                if (text)  out += `${indent}                    <div style="font-size:13px; opacity:0.9;">${text}</div>\n`;
                out += `${indent}                </div>\n`;
            }
            out += `${indent}            ${closeTag}\n`;
            out += `${indent}        </div>\n`;
        });
        out += `${indent}    </div>\n`;

        if (pagination) out += `${indent}    <div class="swiper-pagination"></div>\n`;
        if (navigation) {
            out += `${indent}    <div class="swiper-button-prev"></div>\n`;
            out += `${indent}    <div class="swiper-button-next"></div>\n`;
        }
        out += `${indent}</div>\n`;

        // Swiper初期化JS / Swiper init JS
        const opts = [];
        opts.push(`speed: ${speed}`);
        opts.push(`loop: ${loop}`);
        opts.push(`slidesPerView: ${slidesPerView}`);
        if (slidesPerView > 1) opts.push(`spaceBetween: 10`);
        opts.push(`effect: '${swiperEffect}'`);
        if (useFade)  opts.push(`fadeEffect: { crossFade: true }`);
        if (useCube)  opts.push(`cubeEffect: { shadow: true, slideShadows: true, shadowOffset: 20, shadowScale: 0.94 }`);
        if (useCover) opts.push(`coverflowEffect: { rotate: 30, stretch: 0, depth: 100, modifier: 1, slideShadows: true }`);
        if (useCards) opts.push(`cardsEffect: { perSlideOffset: 8, perSlideRotate: 2 }`);

        if (autoplay) {
            opts.push(`autoplay: { delay: ${delay}, disableOnInteraction: false }`);
        }
        if (pagination) opts.push(`pagination: { el: '#${id} .swiper-pagination', clickable: true }`);
        if (navigation) opts.push(`navigation: { nextEl: '#${id} .swiper-button-next', prevEl: '#${id} .swiper-button-prev' }`);

        const initJs = `
        if (typeof Swiper !== 'undefined') {
            new Swiper('#${id}', {
                ${opts.join(',\n                ')}
            });
        }`;
        r.dynamicJs.push(initJs);

        return out;
    }

    // 記事グリッド。CSSグリッドでカードを並べる。sliderMode の場合は Swiper で横スクロール。
    // Article grid. Lays cards out with CSS grid; sliderMode uses Swiper for horizontal scroll.
    // 768px以下は1カラムに自動で折り返す（レスポンシブ）。
    // Collapses to a single column at 768px and below (responsive).
export function renderArticleGrid(r, id, animClass, baseStyle, props, indent) {
        const g = props.grid || {};
        const items      = Array.isArray(g.items) ? g.items : [];
        const columns    = g.columns ?? 3;
        const gap        = g.gap ?? 20;
        const cardRadius = g.cardRadius ?? 8;
        const arrowColor = g.arrowColor || '#27ae60';
        const imgRatio   = g.imgRatio || '16/10';
        const cardPadding = g.cardPadding ?? 18;
        const sliderMode = g.sliderMode ?? false;

        if (items.length === 0) {
            return `${indent}<div id="${id}" style="${baseStyle} background:#f1f2f6; color:#666; display:flex; align-items:center; justify-content:center;">アイテムが設定されていません</div>\n`;
        }

        // 1枚のカードHTMLを生成する共通関数 / Shared helper that builds one card's HTML.
        const buildCard = (it) => {
            const img   = it.image ? escapeHtml(resolveImageSrc(it.image, r.imageMap)) : '';
            const title = escapeHtml(it.title || '');
            const text  = escapeHtml(it.text  || '');

            let href = '', target = '';
            if (it.linkType === 'url' && it.link) {
                href = escapeHtml(it.link);
                target = ' target="_blank" rel="noopener noreferrer"';
            } else if (it.linkType === 'page' && it.link) {
                href = escapeHtml(it.link);
            }
            const isLink = !!href;

            const cardStyle = `position: relative; background: #ffffff; border-radius: ${cardRadius}px; overflow: hidden; box-shadow: 0 2px 8px rgba(0,0,0,0.06); transition: transform 0.2s, box-shadow 0.2s; height:100%; ${isLink ? 'cursor: pointer;' : ''}`;
            const tag = isLink ? 'a' : 'div';
            const linkAttrs = isLink ? `href="${href}"${target} style="text-decoration: none; color: inherit; display: block; height:100%;"` : '';

            let c = `<${tag} ${linkAttrs} class="article-card" style="${cardStyle}">`;
            if (img && imgRatio !== 'none') {
                c += `<div style="width:100%; aspect-ratio: ${imgRatio}; overflow:hidden;"><img src="${img}" style="width:100%; height:100%; object-fit:cover; display:block;" alt="${title}"></div>`;
            } else if (img) {
                c += `<div style="width:100%; overflow:hidden;"><img src="${img}" style="width:100%; height:auto; object-fit:cover; display:block;" alt="${title}"></div>`;
            }
            c += `<div style="padding: ${cardPadding}px ${cardPadding + 2}px ${cardPadding + 32}px ${cardPadding + 2}px;">`;
            if (title) c += `<div style="font-size: 17px; font-weight: bold; color: #222; margin-bottom: 10px; line-height: 1.4;">${title}</div>`;
            if (text)  c += `<div style="font-size: 13px; color: #666; line-height: 1.6;">${text}</div>`;
            c += `</div>`;
            if (isLink) {
                c += `<div style="position:absolute; right:14px; bottom:14px; width:36px; height:36px; background:${arrowColor}; border-radius:50%; display:flex; align-items:center; justify-content:center; color:#fff; font-size:18px; line-height:1;">→</div>`;
            }
            c += `</${tag}>`;
            return c;
        };

        // ホバー効果は共通 / Hover effect is shared by both modes.
        r.dynamicCss.push(
            `#${id} .article-card:hover { transform: translateY(-4px); box-shadow: 0 8px 20px rgba(0,0,0,0.12); }`
        );

        if (sliderMode) {
            // ===== スライダーモード（Swiperで横スクロール）=====
            // ===== Slider mode (horizontal scroll via Swiper) =====
            const autoplay   = g.autoplay ?? false;
            const delay      = g.delay ?? 3000;
            const loop       = g.loop ?? true;
            const navigation = g.navigation ?? true;

            let out = `${indent}<div id="${id}" class="swiper ${animClass}" style="${baseStyle} padding: 0 ${navigation ? '44px' : '0'};">\n`;
            out += `${indent}    <div class="swiper-wrapper" style="padding-bottom:4px;">\n`;
            items.forEach(it => {
                out += `${indent}        <div class="swiper-slide" style="height:auto;">${buildCard(it)}</div>\n`;
            });
            out += `${indent}    </div>\n`;
            if (navigation) {
                out += `${indent}    <div class="swiper-button-prev" style="color:${arrowColor};"></div>\n`;
                out += `${indent}    <div class="swiper-button-next" style="color:${arrowColor};"></div>\n`;
            }
            out += `${indent}</div>\n`;

            const opts = [];
            opts.push(`slidesPerView: ${columns}`);
            opts.push(`spaceBetween: ${gap}`);
            opts.push(`loop: ${loop}`);
            if (autoplay) opts.push(`autoplay: { delay: ${delay}, disableOnInteraction: false }`);
            if (navigation) opts.push(`navigation: { nextEl: '#${id} .swiper-button-next', prevEl: '#${id} .swiper-button-prev' }`);
            // レスポンシブ: 狭い画面では表示枚数を減らす / Responsive: show fewer slides on narrow screens.
            opts.push(`breakpoints: { 0: { slidesPerView: 1 }, 600: { slidesPerView: ${Math.min(2, columns)} }, 900: { slidesPerView: ${columns} } }`);

            r.dynamicJs.push(`
        if (typeof Swiper !== 'undefined') {
            new Swiper('#${id}', {
                ${opts.join(',\n                ')}
            });
        }`);

            return out;
        }

        // ===== 通常グリッドモード / Normal grid mode =====
        const containerStyle = `${baseStyle} display: grid; grid-template-columns: repeat(${columns}, 1fr); gap: ${gap}px; padding: 0; box-sizing: border-box; align-items: start;`;
        let out = `${indent}<div id="${id}" class="${animClass}" style="${containerStyle}">\n`;
        items.forEach(it => {
            out += `${indent}    ${buildCard(it)}\n`;
        });
        out += `${indent}</div>\n`;

        // モバイル対応: 768px以下は1カラム / Mobile: single column at 768px and below.
        r.dynamicCss.push(
            `@media (max-width: 768px) { #${id} { grid-template-columns: 1fr !important; } }`
        );

        return out;
    }

    // アコーディオン（開閉Q&A）。各項目のヘッダー/本文を出力し、開閉JSを dynamicJs に積む。
    // Accordion (collapsible Q&A). Emits each item's header/body and pushes the toggle JS to dynamicJs.
export function renderAccordion(r, id, animClass, baseStyle, props, indent) {
        const a = props.accordion || {};
        const items = Array.isArray(a.items) ? a.items : [];
        const headerColor = a.headerColor || '#2c3e50';
        const headerBg    = a.headerBg || '#f7f9fa';
        const bodyColor   = a.bodyColor || '#555555';
        const openFirst   = a.openFirst ?? true;

        if (items.length === 0) {
            return `${indent}<div id="${id}" style="${baseStyle} background:#f1f2f6; color:#666; display:flex; align-items:center; justify-content:center;">項目が設定されていません</div>\n`;
        }

        let out = `${indent}<div id="${id}" class="accordion ${animClass}" style="${baseStyle} background:transparent;">\n`;
        items.forEach((it, idx) => {
            const title   = escapeHtml(it.title || '');
            const content = escapeHtml(it.content || '').replace(/\n/g, '<br>');
            const isOpen  = openFirst && idx === 0;

            out += `${indent}    <div class="acc-item" style="border:1px solid #e0e0e0; border-radius:6px; margin-bottom:8px; overflow:hidden;">\n`;
            out += `${indent}        <button class="acc-header" aria-expanded="${isOpen}" style="width:100%; text-align:left; padding:16px 20px; background:${headerBg}; color:${headerColor}; border:none; font-size:16px; font-weight:bold; cursor:pointer; display:flex; justify-content:space-between; align-items:center;">\n`;
            out += `${indent}            <span>${title}</span>\n`;
            out += `${indent}            <span class="acc-icon" style="transition:transform 0.3s; transform:rotate(${isOpen ? '180deg' : '0deg'});">▼</span>\n`;
            out += `${indent}        </button>\n`;
            out += `${indent}        <div class="acc-body" style="max-height:${isOpen ? '500px' : '0'}; overflow:hidden; transition:max-height 0.3s ease;">\n`;
            out += `${indent}            <div style="padding:16px 20px; color:${bodyColor}; font-size:14px; line-height:1.7;">${content}</div>\n`;
            out += `${indent}        </div>\n`;
            out += `${indent}    </div>\n`;
        });
        out += `${indent}</div>\n`;

        // 開閉JS / Open/close (toggle) JS
        r.dynamicJs.push(`
        (function() {
            var acc = document.getElementById('${id}');
            if (!acc) return;
            acc.querySelectorAll('.acc-header').forEach(function(btn) {
                btn.addEventListener('click', function() {
                    var body = btn.nextElementSibling;
                    var icon = btn.querySelector('.acc-icon');
                    var isOpen = btn.getAttribute('aria-expanded') === 'true';
                    if (isOpen) {
                        body.style.maxHeight = '0';
                        btn.setAttribute('aria-expanded', 'false');
                        if (icon) icon.style.transform = 'rotate(0deg)';
                    } else {
                        body.style.maxHeight = body.scrollHeight + 'px';
                        btn.setAttribute('aria-expanded', 'true');
                        if (icon) icon.style.transform = 'rotate(180deg)';
                    }
                });
            });
        })();`);

        return out;
    }
