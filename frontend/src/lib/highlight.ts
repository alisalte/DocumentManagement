/**
 * Search highlights arrive as HTML: the engine escapes the document text and wraps matches in
 * <mark>. Rather than trusting that string with innerHTML, it is split back into plain text
 * segments here, so nothing from a document can ever become markup in the page.
 */
export interface HighlightSegment {
  text: string;
  marked: boolean;
}

const entities: Record<string, string> = {
  '&amp;': '&',
  '&lt;': '<',
  '&gt;': '>',
  '&quot;': '"',
  '&#x27;': "'",
  '&#39;': "'",
  '&#x2F;': '/',
  '&#47;': '/',
};

function decode(text: string): string {
  return text.replace(/&(?:amp|lt|gt|quot|#x27|#39|#x2F|#47);/g, (entity) => entities[entity] ?? entity);
}

export function parseHighlight(fragment: string): HighlightSegment[] {
  const segments: HighlightSegment[] = [];
  let marked = false;
  for (const part of fragment.split(/(<mark>|<\/mark>)/)) {
    if (part === '<mark>') {
      marked = true;
    } else if (part === '</mark>') {
      marked = false;
    } else if (part) {
      segments.push({ text: decode(part), marked });
    }
  }

  return segments;
}
