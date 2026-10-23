// Copies the repository's docs/ Markdown into the Starlight content tree.
//
// docs/<Name>.md        -> src/content/docs/<name>.md         (English, root locale)
// docs/<Name>.ko.md     -> src/content/docs/ko/<name>.md      (Korean)
// docs/markup/<Name>.md -> src/content/docs/markup/<name>.md  (generated reference, English only)
// docs/assets/**        -> public/assets/**
//
// Each page gets frontmatter (title from the first H1, optional description, explicit slug) and its
// relative Markdown links are rewritten to site paths or to GitHub for paths outside docs/.
// The run is idempotent: every synced file is removed first, hand-written pages (index.mdx,
// ko/index.mdx) and the api/ tree (filled by tools/docs/build_api_docs.cmd) are left alone.

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const SITE_BASE = (process.env.PUBLIC_SITE_BASE ?? '/MewUI').trim().replace(/\/+$/, '');
const GITHUB_BLOB = 'https://github.com/aprillz/MewUI/blob/dev/';
const GITHUB_TREE = 'https://github.com/aprillz/MewUI/tree/dev/';
const MAX_DESCRIPTION_LENGTH = 160;

const websiteRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const repoRoot = path.resolve(websiteRoot, '..');
const docsRoot = path.join(repoRoot, 'docs');
const contentRoot = path.join(websiteRoot, 'src', 'content', 'docs');
const assetsOut = path.join(websiteRoot, 'public', 'assets');

const keepFiles = new Set(['index.mdx', 'ko/index.mdx']);
const keepDirs = new Set(['api']);

function toPosix(filePath) {
  return filePath.split(path.sep).join('/');
}

/** Site slug for a docs-relative path such as "Installation.md", "Installation.ko.md" or "markup/Button.md". */
function slugForDocsPath(docsRelative) {
  const posix = toPosix(docsRelative);
  const withoutExtension = posix.replace(/\.md$/i, '');
  if (withoutExtension.startsWith('markup/')) {
    return withoutExtension.toLowerCase();
  }
  if (withoutExtension.endsWith('.ko')) {
    return 'ko/' + withoutExtension.slice(0, -3).toLowerCase();
  }
  return withoutExtension.toLowerCase();
}

function removeSyncedContent() {
  if (!fs.existsSync(contentRoot)) {
    return;
  }
  const walk = (dir) => {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      const relative = toPosix(path.relative(contentRoot, full));
      if (entry.isDirectory()) {
        if (keepDirs.has(relative)) {
          continue;
        }
        walk(full);
        if (fs.readdirSync(full).length === 0) {
          fs.rmdirSync(full);
        }
      } else if (!keepFiles.has(relative)) {
        fs.unlinkSync(full);
      }
    }
  };
  walk(contentRoot);
}

function yamlString(value) {
  return JSON.stringify(value);
}

function plainText(markdown) {
  return markdown
    .replace(/!\[[^\]]*\]\([^)]*\)/g, '')
    .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/[`*_]/g, '')
    .replace(/\s+/g, ' ')
    .trim();
}

function extractTitleAndDescription(lines) {
  let title = null;
  let titleIndex = -1;
  for (let index = 0; index < lines.length; index++) {
    const match = /^#\s+(.+?)\s*#*\s*$/.exec(lines[index]);
    if (match) {
      title = match[1].trim();
      titleIndex = index;
      break;
    }
  }
  let description = null;
  if (titleIndex >= 0) {
    let index = titleIndex + 1;
    while (index < lines.length && lines[index].trim() === '') {
      index++;
    }
    const paragraph = [];
    while (index < lines.length && lines[index].trim() !== '') {
      paragraph.push(lines[index]);
      index++;
    }
    const first = paragraph[0] ?? '';
    const isProse = paragraph.length > 0 && !/^(#|<|```|\||-|\*|>|\d+\.)/.test(first.trim());
    if (isProse) {
      const text = plainText(paragraph.join(' '));
      if (text.length > 0 && text.length <= MAX_DESCRIPTION_LENGTH) {
        description = text;
      }
    }
  }
  return { title, titleIndex, description };
}

/** Rewrites one link target found in a page located at sourceDir (absolute path under docs/). */
function rewriteTarget(target, sourceDir) {
  if (/^(https?:|mailto:|#|\/)/i.test(target) || target.startsWith('//')) {
    return target;
  }
  const hashIndex = target.indexOf('#');
  const anchor = hashIndex >= 0 ? target.slice(hashIndex) : '';
  const pathPart = hashIndex >= 0 ? target.slice(0, hashIndex) : target;
  if (pathPart === '') {
    return target;
  }
  const absolute = path.resolve(sourceDir, decodeURI(pathPart));
  const repoRelative = toPosix(path.relative(repoRoot, absolute));
  if (repoRelative.startsWith('..')) {
    return target;
  }
  if (repoRelative.startsWith('docs/')) {
    const docsRelative = repoRelative.slice('docs/'.length);
    if (docsRelative.startsWith('assets/')) {
      return `${SITE_BASE}/${docsRelative}${anchor}`;
    }
    if (/\.md$/i.test(docsRelative)) {
      return `${SITE_BASE}/${slugForDocsPath(docsRelative)}/${anchor}`;
    }
  }
  const isDirectory = fs.existsSync(absolute) && fs.statSync(absolute).isDirectory();
  return `${isDirectory ? GITHUB_TREE : GITHUB_BLOB}${repoRelative}${anchor}`;
}

function rewriteLinks(lines, sourceDir) {
  let inFence = false;
  return lines.map((line) => {
    if (/^\s*(```|~~~)/.test(line)) {
      inFence = !inFence;
      return line;
    }
    if (inFence) {
      return line;
    }
    return line.replace(/(!?\[[^\]]*\]\()([^)\s]+)((?:\s+"[^"]*")?\))/g, (whole, open, target, close) => {
      return `${open}${rewriteTarget(target, sourceDir)}${close}`;
    });
  });
}

function convertPage(sourceFile, docsRelative) {
  const raw = fs.readFileSync(sourceFile, 'utf8').replace(/^﻿/, '').replace(/\r\n?/g, '\n');
  const lines = raw.split('\n');
  const { title, titleIndex, description } = extractTitleAndDescription(lines);
  if (title === null) {
    throw new Error(`No H1 heading in ${docsRelative}`);
  }
  const body = lines.filter((line, index) => index !== titleIndex);
  const slug = slugForDocsPath(docsRelative);
  const frontmatter = [`title: ${yamlString(title)}`];
  if (description !== null) {
    frontmatter.push(`description: ${yamlString(description)}`);
  }
  frontmatter.push(`slug: ${yamlString(slug)}`);
  const sidebarLabel = /\s+markup reference$/i.test(title) ? title.replace(/\s+markup reference$/i, '') : null;
  if (sidebarLabel !== null) {
    frontmatter.push(`sidebar:`, `  label: ${yamlString(sidebarLabel)}`);
  }
  const rewritten = rewriteLinks(body, path.dirname(sourceFile));
  const content = ['---', ...frontmatter, '---', ...rewritten].join('\n').replace(/\n*$/, '\n');
  const outFile = path.join(contentRoot, ...slug.split('/')) + '.md';
  fs.mkdirSync(path.dirname(outFile), { recursive: true });
  fs.writeFileSync(outFile, content, 'utf8');
  return slug;
}

function syncPages() {
  const synced = [];
  for (const entry of fs.readdirSync(docsRoot, { withFileTypes: true })) {
    if (entry.isFile() && /\.md$/i.test(entry.name)) {
      synced.push(convertPage(path.join(docsRoot, entry.name), entry.name));
    }
  }
  const markupRoot = path.join(docsRoot, 'markup');
  if (fs.existsSync(markupRoot)) {
    for (const entry of fs.readdirSync(markupRoot, { withFileTypes: true })) {
      if (entry.isFile() && /\.md$/i.test(entry.name)) {
        synced.push(convertPage(path.join(markupRoot, entry.name), 'markup/' + entry.name));
      }
    }
  }
  return synced;
}

function syncAssets() {
  fs.rmSync(assetsOut, { recursive: true, force: true });
  const assetsIn = path.join(docsRoot, 'assets');
  if (!fs.existsSync(assetsIn)) {
    return 0;
  }
  fs.cpSync(assetsIn, assetsOut, { recursive: true });
  let count = 0;
  const walk = (dir) => {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      if (entry.isDirectory()) {
        walk(path.join(dir, entry.name));
      } else {
        count++;
      }
    }
  };
  walk(assetsOut);
  return count;
}

removeSyncedContent();
const pages = syncPages();
const assetCount = syncAssets();
console.log(`sync-content: ${pages.length} pages, ${assetCount} assets -> ${toPosix(path.relative(websiteRoot, contentRoot))}`);
