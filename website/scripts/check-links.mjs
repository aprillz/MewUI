// Scans dist/**/*.html for internal hrefs under the site base and reports targets that no built
// file satisfies (a directory index, the file itself, or <path>.html). Exits 1 when any is missing.
// Targets under <base>/api/ and <base>/Gallery/ are skipped when those trees are absent in dist,
// because the API reference and the Gallery are staged into the site by the Pages workflow.

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const SITE_BASE = (process.env.PUBLIC_SITE_BASE ?? '/MewUI').trim().replace(/\/+$/, '') + '/';

const websiteRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const distRoot = path.join(websiteRoot, 'dist');

if (!fs.existsSync(distRoot)) {
  console.error('check-links: dist/ not found; run `npm run build` first');
  process.exit(1);
}

const externalTrees = ['api', 'Gallery'].filter((name) => !fs.existsSync(path.join(distRoot, name)));

function collectHtml(dir, out) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      collectHtml(full, out);
    } else if (/\.html$/i.test(entry.name)) {
      out.push(full);
    }
  }
  return out;
}

function targetExists(sitePath) {
  const relative = sitePath.slice(SITE_BASE.length);
  const segments = relative.split('/').filter((segment) => segment.length > 0).map((segment) => decodeURIComponent(segment));
  const candidate = path.join(distRoot, ...segments);
  if (sitePath.endsWith('/') || relative === '') {
    return fs.existsSync(path.join(candidate, 'index.html'));
  }
  if (fs.existsSync(candidate)) {
    return fs.statSync(candidate).isFile() || fs.existsSync(path.join(candidate, 'index.html'));
  }
  return fs.existsSync(candidate + '.html');
}

const htmlFiles = collectHtml(distRoot, []);
const missing = new Map();
const checked = new Set();
let skipped = 0;

for (const file of htmlFiles) {
  const html = fs.readFileSync(file, 'utf8');
  const pattern = /href\s*=\s*["']([^"']+)["']/gi;
  let match;
  while ((match = pattern.exec(html)) !== null) {
    let href = match[1];
    const hashIndex = href.indexOf('#');
    if (hashIndex >= 0) {
      href = href.slice(0, hashIndex);
    }
    const queryIndex = href.indexOf('?');
    if (queryIndex >= 0) {
      href = href.slice(0, queryIndex);
    }
    if (href === SITE_BASE.slice(0, -1)) {
      href = SITE_BASE;
    }
    if (!href.startsWith(SITE_BASE)) {
      // A root-absolute href outside the base would 404 on the deployed site.
      if (href.startsWith('/') && !href.startsWith('//')) {
        const sources = missing.get(href) ?? new Set();
        sources.add(path.relative(distRoot, file).split(path.sep).join('/'));
        missing.set(href, sources);
      }
      continue;
    }
    // api/ and Gallery/ are staged by the Pages workflow; a locale prefix in front of api/ is a
    // Starlight fallback page of the same tree.
    const segments = href.slice(SITE_BASE.length).split('/');
    const treeSegment = segments[0] === 'ko' ? segments[1] : segments[0];
    if (externalTrees.includes(treeSegment)) {
      skipped++;
      continue;
    }
    checked.add(href);
    if (!targetExists(href)) {
      const sources = missing.get(href) ?? new Set();
      sources.add(path.relative(distRoot, file).split(path.sep).join('/'));
      missing.set(href, sources);
    }
  }
}

if (missing.size > 0) {
  console.error(`check-links: ${missing.size} missing target(s) in ${htmlFiles.length} pages`);
  for (const [href, sources] of [...missing.entries()].sort()) {
    const list = [...sources].slice(0, 5).join(', ');
    const more = sources.size > 5 ? ` (+${sources.size - 5} more)` : '';
    console.error(`  ${href}  <- ${list}${more}`);
  }
  process.exit(1);
}

const skippedNote = externalTrees.length > 0 ? `, skipped ${skipped} link(s) into absent ${externalTrees.join('/')}` : '';
console.log(`check-links: ${checked.size} internal targets in ${htmlFiles.length} pages, all present${skippedNote}`);
