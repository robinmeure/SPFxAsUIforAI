import type { PassageCitation } from './api';

interface MarkdownNode { type: string; value?: string; url?: string; children?: MarkdownNode[]; }

/** Transform text nodes only: code, existing links and images must not become citation controls. */
export const citationLinks = (): ((tree: MarkdownNode) => void) => {
  const visit = (node: MarkdownNode): void => {
    if (!node.children || ['link', 'linkReference', 'code', 'inlineCode', 'image'].indexOf(node.type) >= 0) return;
    const children: MarkdownNode[] = [];
    for (const child of node.children) {
      if (child.type !== 'text' || !child.value) { visit(child); children.push(child); continue; }
      const pattern = /\[(\d{1,3})\]/g;
      let offset = 0;
      let match: RegExpExecArray | null;
      while ((match = pattern.exec(child.value)) !== null) {
        children.push({ type: 'text', value: child.value.slice(offset, match.index) });
        children.push({ type: 'link', url: `#citation-${match[1]}`, children: [{ type: 'text', value: match[0] }] });
        offset = pattern.lastIndex;
      }
      children.push({ type: 'text', value: child.value.slice(offset) });
    }
    node.children = children;
  };
  return visit;
};

export const sourceLabel = (source: string): string =>
  source === 'sharepoint' ? 'SPO' : source === 'upload' ? 'Local' : 'Source unknown';

export const citationLocation = (citation: PassageCitation): string =>
  [citation.section, citation.pageNumber ? `Page ${citation.pageNumber}` : undefined,
    `Passage ${citation.chunkIndex + 1}`].filter(Boolean).join(' · ');

export const safeSourceUrl = (url?: string): string | undefined => {
  if (!url) return undefined;
  try { const parsed = new URL(url); return parsed.protocol === 'https:' ? parsed.href : undefined; }
  catch { return undefined; }
};
