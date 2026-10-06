import { marked, Token, Tokens } from 'marked';

/**
 * Markdown içeriğini GERÇEK Word (.docx) paragraflarına dönüştürür.
 *
 * Not: html-docx-js gibi kütüphaneler içeriği "altChunk" (gömülü HTML) olarak koyar;
 * Word Online, Google Docs, LibreOffice ve Windows önizleme bunu göstermez ve
 * belge BOŞ görünür. Bu yardımcı ise `docx` kütüphanesiyle yerel paragraflar üretir.
 */
export interface DocxSection {
  heading?: string;
  markdown: string;
}

export interface DocxExportOptions {
  title: string;
  subtitle?: string;
  sections: DocxSection[];
  pageBreakBetweenSections?: boolean;
}

interface RunStyle {
  bold?: boolean;
  italics?: boolean;
  strike?: boolean;
  code?: boolean;
  color?: string;
  underline?: boolean;
}

function decodeEntities(text: string): string {
  return (text || '')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'")
    .replace(/&#039;/g, "'")
    .replace(/&nbsp;/g, ' ')
    .replace(/&amp;/g, '&');
}

export async function markdownToDocxBlob(options: DocxExportOptions): Promise<Blob> {
  const docx = await import('docx');
  const {
    Document, Packer, Paragraph, TextRun, HeadingLevel, Table, TableRow, TableCell,
    WidthType, BorderStyle, ShadingType
  } = docx;

  const headingLevels = [
    HeadingLevel.HEADING_1, HeadingLevel.HEADING_2, HeadingLevel.HEADING_3,
    HeadingLevel.HEADING_4, HeadingLevel.HEADING_5, HeadingLevel.HEADING_6
  ];

  // ── Inline token → TextRun[] ──
  const inlineRuns = (tokens: Token[] | undefined, style: RunStyle = {}): any[] => {
    const runs: any[] = [];
    if (!tokens) return runs;

    for (const t of tokens as any[]) {
      switch (t.type) {
        case 'strong':
          runs.push(...inlineRuns(t.tokens, { ...style, bold: true }));
          break;
        case 'em':
          runs.push(...inlineRuns(t.tokens, { ...style, italics: true }));
          break;
        case 'del':
          runs.push(...inlineRuns(t.tokens, { ...style, strike: true }));
          break;
        case 'codespan':
          runs.push(makeRun(decodeEntities(t.text), { ...style, code: true }));
          break;
        case 'link':
          runs.push(...inlineRuns(t.tokens, { ...style, color: '1D4ED8', underline: true }));
          if (t.href && t.href !== t.text) {
            runs.push(makeRun(` (${t.href})`, { ...style, color: '64748B' }));
          }
          break;
        case 'br':
          runs.push(new TextRun({ text: '', break: 1 }));
          break;
        case 'text':
          if (t.tokens && t.tokens.length) {
            runs.push(...inlineRuns(t.tokens, style));
          } else {
            runs.push(...textWithBreaks(decodeEntities(t.text), style));
          }
          break;
        case 'escape':
          runs.push(makeRun(decodeEntities(t.text), style));
          break;
        case 'html':
          runs.push(makeRun(decodeEntities((t.text || '').replace(/<[^>]+>/g, '')), style));
          break;
        case 'image':
          runs.push(makeRun(`[Görsel: ${t.text || t.href}]`, { ...style, italics: true }));
          break;
        default:
          if (t.tokens) runs.push(...inlineRuns(t.tokens, style));
          else if (t.text) runs.push(makeRun(decodeEntities(t.text), style));
          else if (t.raw) runs.push(makeRun(t.raw, style));
      }
    }
    return runs;
  };

  const textWithBreaks = (text: string, style: RunStyle): any[] => {
    const parts = text.split('\n');
    return parts.map((p, i) => new TextRun({ ...runProps(style), text: p, break: i > 0 ? 1 : undefined }));
  };

  const runProps = (style: RunStyle) => ({
    bold: style.bold,
    italics: style.italics,
    strike: style.strike,
    color: style.color,
    underline: style.underline ? {} : undefined,
    font: style.code ? 'Consolas' : undefined,
    shading: style.code ? { type: ShadingType.CLEAR, color: 'auto', fill: 'F1F5F9' } : undefined
  });

  const makeRun = (text: string, style: RunStyle) => new TextRun({ ...runProps(style), text });

  // ── Block token → (Paragraph | Table)[] ──
  const blocks = (tokens: Token[], listLevel = 0, quote = false): any[] => {
    const out: any[] = [];

    for (const t of tokens as any[]) {
      switch (t.type) {
        case 'heading':
          out.push(new Paragraph({
            heading: headingLevels[Math.min(Math.max(t.depth, 1), 6) - 1],
            children: inlineRuns(t.tokens),
            spacing: { before: 240, after: 120 }
          }));
          break;

        case 'paragraph':
          out.push(new Paragraph({
            children: inlineRuns(t.tokens, quote ? { italics: true, color: '475569' } : {}),
            spacing: { after: 120 },
            indent: quote ? { left: 567 } : undefined
          }));
          break;

        case 'text':
          // Liste öğesi içindeki sıkı (tight) metin
          out.push(new Paragraph({ children: inlineRuns(t.tokens ?? [t]), spacing: { after: 60 } }));
          break;

        case 'list':
          out.push(...listBlocks(t as Tokens.List, listLevel));
          break;

        case 'blockquote':
          out.push(...blocks(t.tokens, listLevel, true));
          break;

        case 'code':
          for (const line of String(t.text || '').split('\n')) {
            out.push(new Paragraph({
              children: [makeRun(line || ' ', { code: true })],
              spacing: { after: 0 }
            }));
          }
          out.push(new Paragraph({ children: [] }));
          break;

        case 'hr':
          out.push(new Paragraph({
            children: [],
            border: { bottom: { color: 'CBD5E1', space: 1, style: BorderStyle.SINGLE, size: 6 } },
            spacing: { before: 120, after: 240 }
          }));
          break;

        case 'table':
          out.push(tableBlock(t as Tokens.Table));
          out.push(new Paragraph({ children: [] }));
          break;

        case 'html': {
          const plain = decodeEntities(String(t.text || '').replace(/<[^>]+>/g, '')).trim();
          if (plain) out.push(new Paragraph({ children: [makeRun(plain, {})] }));
          break;
        }

        case 'space':
          break;

        default:
          if (t.tokens) out.push(new Paragraph({ children: inlineRuns(t.tokens) }));
          else if (t.text) out.push(new Paragraph({ children: [makeRun(decodeEntities(t.text), {})] }));
      }
    }
    return out;
  };

  const listBlocks = (list: Tokens.List, level: number): any[] => {
    const out: any[] = [];
    let num = typeof list.start === 'number' ? list.start : 1;

    for (const item of list.items as any[]) {
      const children: Token[] = item.tokens || [];
      let firstDone = false;

      for (const child of children as any[]) {
        if (child.type === 'list') {
          out.push(...listBlocks(child, level + 1));
          continue;
        }
        if (child.type === 'space') continue;

        const runs = child.type === 'text' || child.type === 'paragraph'
          ? inlineRuns(child.tokens ?? [child])
          : null;

        if (runs && !firstDone) {
          if (list.ordered) {
            out.push(new Paragraph({
              children: [makeRun(`${num}. `, { bold: true }), ...runs],
              indent: { left: 360 * (level + 1), hanging: 300 },
              spacing: { after: 60 }
            }));
          } else {
            out.push(new Paragraph({
              children: runs,
              bullet: { level: Math.min(level, 8) },
              spacing: { after: 60 }
            }));
          }
          firstDone = true;
        } else if (runs) {
          out.push(new Paragraph({
            children: runs,
            indent: { left: 360 * (level + 1) + 360 },
            spacing: { after: 60 }
          }));
        } else {
          out.push(...blocks([child], level + 1));
        }
      }
      num++;
    }
    return out;
  };

  const tableBlock = (t: Tokens.Table): any => {
    const cell = (tokens: Token[], header: boolean) => new TableCell({
      children: [new Paragraph({ children: inlineRuns(tokens, header ? { bold: true } : {}) })],
      shading: header ? { type: ShadingType.CLEAR, color: 'auto', fill: 'E2E8F0' } : undefined,
      margins: { top: 60, bottom: 60, left: 100, right: 100 }
    });

    const rows = [
      new TableRow({ tableHeader: true, children: t.header.map((h: any) => cell(h.tokens, true)) }),
      ...t.rows.map((r: any[]) => new TableRow({ children: r.map((c: any) => cell(c.tokens, false)) }))
    ];

    return new Table({ rows, width: { size: 100, type: WidthType.PERCENTAGE } });
  };

  // ── Belge gövdesi ──
  const children: any[] = [
    new Paragraph({
      heading: HeadingLevel.TITLE,
      children: [new TextRun({ text: options.title, bold: true, color: '1E3A8A' })],
      spacing: { after: 80 }
    })
  ];

  if (options.subtitle) {
    children.push(new Paragraph({
      children: [new TextRun({ text: options.subtitle, color: '64748B', size: 20 })],
      border: { bottom: { color: '1E3A8A', space: 4, style: BorderStyle.SINGLE, size: 12 } },
      spacing: { after: 240 }
    }));
  }

  options.sections.forEach((section, idx) => {
    if (section.heading) {
      children.push(new Paragraph({
        heading: HeadingLevel.HEADING_1,
        children: [new TextRun({ text: section.heading, color: '1E3A8A' })],
        pageBreakBefore: idx > 0 && options.pageBreakBetweenSections !== false,
        spacing: { before: 240, after: 160 }
      }));
    }

    const md = (section.markdown || '').trim();
    if (!md) {
      children.push(new Paragraph({ children: [new TextRun({ text: 'İçerik bulunamadı.', italics: true, color: '94A3B8' })] }));
      return;
    }

    const tokens = marked.lexer(md, { gfm: true });
    children.push(...blocks(tokens));
  });

  const doc = new Document({
    creator: 'VideoOzet',
    title: options.title,
    styles: {
      default: {
        document: { run: { font: 'Calibri', size: 22 } }
      }
    },
    sections: [{
      properties: {
        page: { margin: { top: 1134, bottom: 1134, left: 1134, right: 1134 } }
      },
      children
    }]
  });

  return Packer.toBlob(doc);
}

/** QC listesini Markdown tablosuna çevirir (docx içinde gerçek tablo olur). */
export function qcToMarkdown(
  items: any[],
  summary: { skor?: number; desteklenen?: number; belirsiz?: number; desteklenmeyen?: number }
): string {
  const esc = (s: any) => String(s ?? '').replace(/\|/g, '\\|').replace(/\r?\n/g, ' ');
  let md = `**Doğruluk Skoru:** %${summary.skor ?? 0}\n\n` +
    `- Desteklenen: ${summary.desteklenen ?? 0}\n` +
    `- Belirsiz: ${summary.belirsiz ?? 0}\n` +
    `- Desteklenmeyen: ${summary.desteklenmeyen ?? 0}\n\n`;

  if (!items || items.length === 0) {
    return md + '_Detaylı QC raporu bulunamadı._\n';
  }

  md += `| # | İddia | Durum | Açıklama |\n|---|---|---|---|\n`;
  items.forEach((it: any, i: number) => {
    md += `| ${i + 1} | ${esc(it.iddia)} | **${esc(it.durum)}** | ${esc(it.aciklama)} |\n`;
  });
  return md;
}

/** Tarayıcıda blob indirir. */
export function saveBlobAs(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  setTimeout(() => URL.revokeObjectURL(url), 1500);
}
