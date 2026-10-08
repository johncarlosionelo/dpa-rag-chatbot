import { useRef, useState } from 'react';
import { AutoHeight } from './anim/auto-height';
import { RippleButton } from './anim/ripple';
import { Shine } from './anim/shine';

const Limit = 'min(30dvh, 220px)';

export function AskBar({ onSend, busy }: { onSend: (question: string) => void; busy: boolean }) {
  const [value, setValue] = useState('');
  const field = useRef<HTMLTextAreaElement>(null);

  const submit = () => {
    if (busy || value.trim().length === 0) return;
    onSend(value);
    setValue('');
    field.current?.focus();
  };

  return (
    <div className="shrink-0 border-t border-line px-5 py-3.5">
      <div className="mx-auto flex w-full max-w-3xl items-end gap-2">
        <AutoHeight
          deps={[value]}
          className="min-w-0 flex-1"
          transition={{ duration: 0.16, ease: [0.22, 1, 0.36, 1] }}
        >
          <div
            className={`rounded-lg border bg-panel transition-colors ${
              busy ? 'border-line opacity-60' : 'border-line focus-within:border-accent/60'
            }`}
          >
            <textarea
              ref={field}
              value={value}
              rows={1}
              placeholder={busy ? 'Reading the Act...' : 'Ask about the Data Privacy Act of 2012'}
              disabled={busy}
              onChange={(event) => setValue(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter' && !event.shiftKey) {
                  event.preventDefault();
                  submit();
                }
              }}
              style={{ maxHeight: Limit }}
              className="block max-h-[min(30dvh,220px)] w-full resize-none overflow-y-auto bg-transparent px-3.5 py-2.5 text-[13.5px] leading-relaxed [field-sizing:content] outline-none placeholder:text-muted/70"
            />
          </div>
        </AutoHeight>

        <RippleButton
          onClick={submit}
          disabled={busy || value.trim().length === 0}
          className="h-[42px] shrink-0 rounded-lg border border-accent/50 bg-accent/15 px-4 text-[13px] text-accent transition-colors hover:bg-accent/25 disabled:opacity-40"
        >
          <Shine color="#ffffff" opacity={0.18} duration={1.5} enableOnHover>
            <span>Ask</span>
          </Shine>
        </RippleButton>
      </div>
    </div>
  );
}
