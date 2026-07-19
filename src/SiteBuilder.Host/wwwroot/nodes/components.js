// ============================================================
// components.js - テンプレート部品（Hero/Card/FAQ/Slider/ArticleGrid/Accordion）の生成
// spawnComponent は elements.js の spawnElement を組み合わせて定形レイアウトを作る。
// ============================================================
import { layer } from '../canvas/canvas.js';
import { saveHistory } from '../history/history.js';
import { spawnElement, makeTypeCounter, applySelectedNodes } from './elements.js';

export function spawnComponent(componentName) {
    let data = null;

    if (componentName === 'Hero') {
        data = {
            type: 'Group',
            transform: { x: 50, y: 50, width: 800, height: 400 },
            properties: { name: 'Hero Component', bgcolor: 'transparent', shadow: 'none', animation: 'fadein', bgimage: '' },
            children: [
                { type: 'Rect',  transform: { x: 0, y: 0, width: 800, height: 400 }, properties: { name: 'Hero BG', bgcolor: '#2c3e50', shadow: 'none', animation: 'none' } },
                { type: 'Label', transform: { x: 50, y: 100, width: 700, height: 60 }, properties: { name: 'Headline', text: 'Catchy Headline Here', color: '#ffffff', fontsize: 48, shadow: 'none', animation: 'none', align: 'left', fontfamily: 'sans-serif' } },
                { type: 'Label', transform: { x: 50, y: 180, width: 700, height: 40 }, properties: { name: 'Subhead', text: 'Short description goes here. Click to edit.', color: '#bdc3c7', fontsize: 24, shadow: 'none', animation: 'none', align: 'left', fontfamily: 'sans-serif' } },
                { type: 'Button',transform: { x: 50, y: 260, width: 200, height: 50 }, properties: { name: 'CTA Button', text: 'Get Started', bgcolor: '#e74c3c', color: '#ffffff', fontsize: 18, shadow: 'light', animation: 'none', align: 'center', fontfamily: 'sans-serif', bgimage: '' } }
            ]
        };
    } else if (componentName === 'Card') {
        data = {
            type: 'Group',
            transform: { x: 50, y: 50, width: 300, height: 400 },
            properties: { name: 'Card Component', bgcolor: 'transparent', shadow: 'none', animation: 'fadeup', bgimage: '' },
            children: [
                { type: 'Rect',  transform: { x: 0, y: 0, width: 300, height: 400 }, properties: { name: 'Card BG', bgcolor: '#ffffff', shadow: 'light', animation: 'none' } },
                { type: 'Image', transform: { x: 0, y: 0, width: 300, height: 160 }, properties: { name: 'Thumbnail', text: 'https://placehold.co/300x160/png', shadow: 'none', animation: 'none' } },
                { type: 'Label', transform: { x: 20, y: 180, width: 260, height: 30 }, properties: { name: 'Card Title', text: 'Card Title', color: '#333333', fontsize: 24, shadow: 'none', animation: 'none', align: 'left', fontfamily: 'sans-serif' } },
                { type: 'Label', transform: { x: 20, y: 220, width: 260, height: 80 }, properties: { name: 'Card Text', text: 'This is a description inside the card component. Edit this text.', color: '#666666', fontsize: 14, shadow: 'none', animation: 'none', align: 'left', fontfamily: 'sans-serif' } },
                { type: 'Button',transform: { x: 20, y: 320, width: 120, height: 40 }, properties: { name: 'Action Button', text: 'Read More', bgcolor: '#3498db', color: '#ffffff', fontsize: 14, shadow: 'none', animation: 'none', align: 'center', fontfamily: 'sans-serif', bgimage: '' } }
            ]
        };
    } else if (componentName === 'FAQ') {
        data = {
            type: 'Group',
            transform: { x: 50, y: 50, width: 600, height: 100 },
            properties: { name: 'FAQ Item', bgcolor: 'transparent', shadow: 'none', animation: 'slideleft', bgimage: '' },
            children: [
                { type: 'Rect',  transform: { x: 0, y: 0, width: 600, height: 100 }, properties: { name: 'FAQ BG', bgcolor: '#f9f9f9', shadow: 'light', animation: 'none' } },
                { type: 'Label', transform: { x: 20, y: 20, width: 560, height: 30 }, properties: { name: 'Question', text: 'Q: What is this component?', color: '#2c3e50', fontsize: 18, shadow: 'none', animation: 'none', align: 'left', fontfamily: 'sans-serif' } },
                { type: 'Label', transform: { x: 20, y: 60, width: 560, height: 30 }, properties: { name: 'Answer', text: 'A: This is a pre-built FAQ layout block.', color: '#7f8c8d', fontsize: 14, shadow: 'none', animation: 'none', align: 'left', fontfamily: 'sans-serif' } }
            ]
        };
    } else if (componentName === 'Slider') {
        data = {
            type: 'Slider',
            transform: { x: 50, y: 50, width: 600, height: 350 },
            properties: { 
                name: 'Image Slider', 
                bgcolor: '#2d3436', 
                shadow: 'light', 
                animation: 'fadein',
                // カンマ区切りで初期画像を3枚設定
                text: 'https://placehold.co/600x350/png?text=Slide+1,https://placehold.co/600x350/png?text=Slide+2,https://placehold.co/600x350/png?text=Slide+3' 
            }
        };
    } else if (componentName === 'ArticleGrid') {
        data = {
            type: 'ArticleGrid',
            transform: { x: 50, y: 50, width: 900, height: 480 },
            properties: {
                name: 'Article Grid',
                bgcolor: 'transparent',
                shadow: 'none',
                animation: 'fadeup',
                grid: {
                    columns: 3,
                    gap: 20,
                    cardRadius: 8,
                    arrowColor: '#27ae60',
                    items: [
                        { image: 'https://placehold.co/400x240/png?text=Article+1', title: '記事タイトル1', text: 'ここに記事の概要が入ります。クリックで詳細ページに遷移します。', linkType: 'none', link: '' },
                        { image: 'https://placehold.co/400x240/png?text=Article+2', title: '記事タイトル2', text: 'ここに記事の概要が入ります。クリックで詳細ページに遷移します。', linkType: 'none', link: '' },
                        { image: 'https://placehold.co/400x240/png?text=Article+3', title: '記事タイトル3', text: 'ここに記事の概要が入ります。クリックで詳細ページに遷移します。', linkType: 'none', link: '' },
                    ]
                }
            }
        };
    } else if (componentName === 'Accordion') {
        data = {
            type: 'Accordion',
            transform: { x: 50, y: 50, width: 600, height: 300 },
            properties: {
                name: 'Accordion',
                bgcolor: '#ffffff',
                shadow: 'light',
                animation: 'fadeup',
                accordion: {
                    headerColor: '#2c3e50',
                    headerBg: '#f7f9fa',
                    bodyColor: '#555555',
                    openFirst: true,
                    items: [
                        { title: '質問1: このサービスは何ですか？', content: 'ここに回答が入ります。クリックすると開閉します。' },
                        { title: '質問2: 料金はいくらですか？', content: 'ここに回答が入ります。複数行の説明も入れられます。' },
                        { title: '質問3: サポートはありますか？', content: 'ここに回答が入ります。お気軽にお問い合わせください。' },
                    ]
                }
            }
        };
    }

    if (!data) return;

    const nextNum = makeTypeCounter();
    function assignIds(nodeData) {
        nodeData.id = nodeData.type.toLowerCase() + '_' + nextNum(nodeData.type);
        if (nodeData.children) {
            nodeData.children.forEach(assignIds);
        }
    }
    assignIds(data);

    // data.type に応じて正しい要素を生成（Sliderの場合もある）
    const node = spawnElement(data.type, data, layer, false, true);

    layer.batchDraw();
    applySelectedNodes([node]);
    saveHistory();
}
