import * as React from 'react';
import { AnimatePresence, motion } from 'motion/react';

import { CheckIcon } from '@/components/anim/CheckIcon';
import { cn } from '@/lib/utils';

type CopyButtonProps = {
  value: string;
  className?: string;
  label?: string;
};

export function CopyButton({ value, className, label = 'Copy' }: CopyButtonProps) {
  const [state, setState] = React.useState<'idle' | 'done' | 'failed'>('idle');
  const timer = React.useRef<ReturnType<typeof setTimeout> | null>(null);

  React.useEffect(() => () => {
    if (timer.current) clearTimeout(timer.current);
  }, []);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(value);
      setState('done');
    } catch {
      setState('failed');
    }

    if (timer.current) clearTimeout(timer.current);
    timer.current = setTimeout(() => setState('idle'), 1600);
  };

  return (
    <button
      type="button"
      onClick={copy}
      aria-label={state === 'done' ? 'Copied' : label}
      className={cn(
        'group relative inline-flex size-7 shrink-0 items-center justify-center overflow-hidden rounded-md border border-line bg-panel/80 text-muted transition-colors hover:border-accent/50 hover:text-ink',
        className,
      )}
    >
      <motion.span
        aria-hidden
        className="pointer-events-none absolute inset-0 bg-accent/15"
        initial={{ opacity: 0 }}
        animate={{ opacity: state === 'done' || state === 'failed' ? 1 : 0 }}
        transition={{ duration: 0.18 }}
      />

      <AnimatePresence mode="wait" initial={false}>
        <motion.span
          key={state}
          className="relative flex items-center justify-center"
          initial={{ opacity: 0, scale: 0.6, rotate: -18 }}
          animate={{ opacity: 1, scale: 1, rotate: 0 }}
          exit={{ opacity: 0, scale: 0.6, rotate: 18 }}
          transition={{ duration: 0.2, ease: [0.22, 1, 0.36, 1] }}
        >
          {state === 'done' ? (
            <CheckIcon size={13} strokeWidth={2.5} />
          ) : state === 'failed' ? (
            <AlertIcon />
          ) : (
            <CopyIcon />
          )}
        </motion.span>
      </AnimatePresence>

      <AnimatePresence>
        {state !== 'idle' && (
          <motion.span
            className="pointer-events-none absolute -top-8 left-1/2 -translate-x-1/2 whitespace-nowrap rounded-md border border-line bg-panel px-2 py-1 font-mono text-[10px] uppercase tracking-widest text-ink shadow-lg"
            initial={{ opacity: 0, y: 4, scale: 0.9 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: 2, scale: 0.96 }}
            transition={{ duration: 0.18, ease: 'easeOut' }}
          >
            {state === 'done' ? 'Copied' : 'Press Ctrl+C'}
          </motion.span>
        )}
      </AnimatePresence>
    </button>
  );
}

function CopyIcon() {
  return (
    <svg
      width="13"
      height="13"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <rect x="9" y="9" width="12" height="12" rx="2" />
      <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" />
    </svg>
  );
}

function AlertIcon() {
  return (
    <svg
      width="13"
      height="13"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2.2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <path d="M12 8v5" />
      <circle cx="12" cy="16.5" r="0.6" fill="currentColor" />
    </svg>
  );
}
