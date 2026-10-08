import { Fragment } from 'react';

const AllSpace = /[ \t]{2,}/g;
const ClauseStart = /\(([a-z])\)\s/g;
const ItemStart = /\((\d{1,2})\)\s/g;
const Lead = /^(?:Provided,?|Provided that)\s*:?\s*/i;
const Bound = /:\s*Provided\b/g;

type Item = { label: string; text: string };
type Clause = { label: string; intro: string; items: Item[] };
type Block = { kind: 'clause'; clause: Clause } | { kind: 'para'; text: string };

const clean = (text: string) =>
  text
    .replace(Bound, '.')
    .replace(AllSpace, ' ')
    .replace(Lead, '')
    .trim();

function parseClause(body: string, label: string): Clause {
  const head = body.replace(ClauseStart, "");
  const parts = head.split(ItemStart);
  const clause: Clause = { label, intro: clean(parts[0]), items: [] };

  for (let index = 1; index < parts.length; index += 2) {
    const text = clean(parts[index + 1] ?? '');
    if (text.length > 0) {
      clause.items.push({ label: parts[index], text });
    }
  }

  return clause;
}

function parse(text: string): Block[] {
  const heads = [...text.matchAll(ClauseStart)];

  if (heads.length === 0) {
    const body = clean(text);
    return body.length > 0 ? [{ kind: 'para', text: body }] : [];
  }

  const blocks: Block[] = [];
  const preamble = clean(text.slice(0, heads[0].index ?? 0));

  if (preamble.length > 0) {
    blocks.push({ kind: 'para', text: preamble });
  }

  for (let index = 0; index < heads.length; index++) {
    const start = heads[index].index ?? 0;
    const end = index + 1 < heads.length ? heads[index + 1].index ?? text.length : text.length;
    const label = heads[index][1];
    blocks.push({ kind: 'clause', clause: parseClause(text.slice(start, end), label) });
  }

  return blocks;
}

export function ActText({ text }: { text: string }) {
  const blocks = parse(text);

  return (
    <div className="flex flex-col gap-2.5 text-[12.5px] leading-relaxed text-muted">
      {blocks.map((block, index) => {
        if (block.kind === 'para') {
          return <p key={index}>{block.text}</p>;
        }

        const { clause } = block;

        return (
          <div key={index} className="flex gap-2">
            <span className="shrink-0 pt-px font-mono text-[11px] text-accent/80">
              ({clause.label})
            </span>

            <div className="min-w-0 flex-1">
              {clause.intro.length > 0 && <p>{clause.intro}</p>}

              {clause.items.length > 0 && (
                <ul className="mt-1.5 flex flex-col gap-1.5">
                  {clause.items.map((item, itemIndex) => (
                    <Fragment key={itemIndex}>
                      <li className="flex gap-2">
                        <span className="shrink-0 pt-px font-mono text-[11px] text-accent/55">
                          ({item.label})
                        </span>
                        <span className="min-w-0 flex-1">{item.text}</span>
                      </li>
                    </Fragment>
                  ))}
                </ul>
              )}
            </div>
          </div>
        );
      })}
    </div>
  );
}
