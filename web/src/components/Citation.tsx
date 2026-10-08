import { useState } from 'react';
import { AnimatePresence, motion } from 'motion/react';
import { AutoHeight } from './anim/auto-height';
import { CheckIcon } from './anim/CheckIcon';
import { ActText } from './ActText';
import type { SourceRef } from '../api';

export function Sources({ sources, label }: { sources: SourceRef[]; label: string }) {
  const [open, setOpen] = useState<string | null>(null);

  const active = sources.find((source) => source.number === open) ?? null;

  return (
    <div className="flex flex-col gap-2">
      <span className="font-mono text-[10px] uppercase tracking-widest text-muted">
        {label}
      </span>

      <div className="flex flex-wrap items-center gap-1.5">
        {sources.map((source) => {
          const isOpen = open === source.number;

          return (
            <button
              key={source.number}
              type="button"
              onClick={() => setOpen(isOpen ? null : source.number)}
              aria-expanded={isOpen}
              className={`inline-flex shrink-0 items-center gap-1.5 rounded-md border px-2 py-1 text-[11.5px] transition-colors ${
                isOpen
                  ? 'border-accent/60 bg-accent/15 text-accent'
                  : 'border-line bg-panel/70 text-ink/85 hover:border-accent/50 hover:text-accent'
              }`}
            >
              <CheckIcon size={12} />
              Section {source.number}
            </button>
          );
        })}
      </div>

      <AnimatePresence initial={false} mode="wait">
        {active && (
          <motion.div
            key={active.number}
            initial={{ opacity: 0, y: -4 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -4 }}
            transition={{ duration: 0.18, ease: [0.22, 1, 0.36, 1] }}
          >
            <AutoHeight transition={{ duration: 0.2, ease: [0.22, 1, 0.36, 1] }}>
              <article className="rounded-lg border border-line bg-panel/60 px-3.5 py-3">
                <header className="flex flex-wrap items-baseline gap-x-2">
                  <span className="font-mono text-[11px] uppercase tracking-widest text-accent">
                    Section {active.number}
                  </span>
                  <h4 className="text-[12.5px] font-medium text-ink/90">{active.title}</h4>
                </header>
                <div className="mt-2 max-h-72 overflow-y-auto overscroll-contain rounded-md bg-canvas/40 px-3 py-2.5 [scrollbar-width:thin]">
                  <ActText text={active.text} />
                </div>
              </article>
            </AutoHeight>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}
