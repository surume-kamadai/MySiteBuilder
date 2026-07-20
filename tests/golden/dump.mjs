// ============================================================
// dump.mjs — JS版出力エンジン（wwwroot/export）でゴールデンフィクスチャを生成する。
// dump.mjs — generate golden fixtures using the JS output engine (wwwroot/export).
//
// 使い方 / Usage:  node tests/golden/dump.mjs
// 生成物 / Output: tests/SiteBuilder.Core.Tests/fixtures/<project>.<static|laravel>.<sep0|sep1>.json
//
// wwwroot の JS は ESM 記法だが同梱 package.json が無いため、そのままでは Node が
// CommonJS として解釈してしまう。ここでは 4ファイルを一時ディレクトリへコピーし、
// {"type":"module"} を添えて動的 import する（wwwroot 本体は無改変）。
// The wwwroot JS uses ESM syntax but ships no package.json, so Node would treat it as
// CommonJS. We copy the four files to a temp dir with {"type":"module"} and import them
// dynamically (wwwroot itself stays untouched).
// ============================================================
import fs from 'fs/promises';
import path from 'path';
import { fileURLToPath, pathToFileURL } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, '../..');
const srcDir = path.join(repoRoot, 'src/SiteBuilder.Host/wwwroot/export');
const projectsDir = path.join(__dirname, 'projects');
const outDir = path.join(repoRoot, 'tests/SiteBuilder.Core.Tests/fixtures');
const tmp = path.join(__dirname, '.jsbuild');

async function loadEngine() {
    await fs.mkdir(tmp, { recursive: true });
    for (const f of ['css-generator.js', 'render-components.js', 'renderer.js', 'exporter.js']) {
        await fs.copyFile(path.join(srcDir, f), path.join(tmp, f));
    }
    await fs.writeFile(path.join(tmp, 'package.json'), JSON.stringify({ type: 'module' }));
    return import(pathToFileURL(path.join(tmp, 'exporter.js')).href);
}

// BuildResult をフィクスチャ形式へ（画像は path のみ比較する）。
// Convert a BuildResult into fixture form (images compared by path only).
function toManifest(result) {
    return {
        projectName: result.projectName,
        files: result.files.map(f => ({ path: f.path, content: f.content })),
        imagePaths: result.images.map(i => i.path),
    };
}

async function main() {
    const { buildStaticProject, buildLaravelProject } = await loadEngine();
    await fs.mkdir(outDir, { recursive: true });

    const projectFiles = (await fs.readdir(projectsDir)).filter(f => f.endsWith('.json'));
    let count = 0;

    for (const pf of projectFiles) {
        const base = pf.replace(/\.json$/, '');
        const raw = JSON.parse(await fs.readFile(path.join(projectsDir, pf), 'utf-8'));

        for (const sep of [false, true]) {
            // separateCss を切り替えたクローン（C#側テストも同じ操作をする）。
            const project = JSON.parse(JSON.stringify(raw));
            project.settings = project.settings || {};
            project.settings.separateCss = sep;
            const sfx = sep ? 'sep1' : 'sep0';

            const staticOut = toManifest(buildStaticProject(project));
            const laravelOut = toManifest(buildLaravelProject(project));

            await fs.writeFile(path.join(outDir, `${base}.static.${sfx}.json`), JSON.stringify(staticOut, null, 2));
            await fs.writeFile(path.join(outDir, `${base}.laravel.${sfx}.json`), JSON.stringify(laravelOut, null, 2));
            count += 2;
        }
    }

    await fs.rm(tmp, { recursive: true, force: true });
    console.log(`generated ${count} golden manifests into ${path.relative(repoRoot, outDir)}`);
}

main().catch(e => { console.error(e); process.exit(1); });
