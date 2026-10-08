import { useState } from 'react';
import { AnimatePresence, motion } from 'motion/react';
import { AutoHeight } from './anim/auto-height';
import { CheckIcon } from './anim/CheckIcon';
import type { SourceRef } from '../api';

export function Citation({ source }: { source: SourceRef }) {
  const [open, setOpen] = useState(false);

  return (
    <>
      <button
        type="button"
        onClick={() => setOpen((current) => !current)}
        aria-expanded={open}
        className="flex items-center gap-1.5 rounded-md border border-line bg-panel/70 px-2 py-1 text-[11.5px] text-ink/85 transition-colors hover:border-accent/50 hover:text-accent"
      >
        <CheckIcon size={12} />
        Section {source.number}
      </button>

      <AnimatePresence>
        {open && (
          <motion.div
            key={`panel-${source.number}`}
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="w-full"
          >
            <AutoHeight>
              <div className="mt-2 rounded-lg border border-line bg-panel/60 px-3.5 py-3">
                <p className="text-[12px] font-medium text-ink/90">{source.title}</p>
                <p className="mt-1.5 text-[12px] leading-relaxed text-muted">
                  {source.text}
                </p>
              </div>
            </AutoHeight>
          </motion.div>
        )}
      </AnimatePresence>
    </>
  );
}
