import { useState } from 'react';
import { RippleButton } from './anim/ripple';
import { Shine } from './anim/shine';

export function AskBar({ onSend }: { onSend: (question: string) => void }) {
  const [value, setValue] = useState('');

  const submit = () => {
    if (value.trim().length === 0) {
      return;
    }

    onSend(value);
    setValue('');
  };

  return (
    <div className="shrink-0 border-t border-line px-5 py-3.5">
      <div className="mx-auto flex w-full max-w-3xl items-end gap-2">
        <textarea
          value={value}
          rows={1}
          placeholder="Ask about the Data Privacy Act of 2012"
          onChange={(event) => setValue(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === 'Enter' && !event.shiftKey) {
              event.preventDefault();
              submit();
            }
          }}
          className="max-h-32 min-h-[42px] w-full resize-none rounded-lg border border-line bg-panel px-3.5 py-2.5 text-[13.5px] leading-relaxed outline-none transition-colors placeholder:text-muted/70 focus:border-accent/60"
        />

        <RippleButton
          onClick={submit}
          disabled={value.trim().length === 0}
          className="shrink-0 rounded-lg border border-accent/50 bg-accent/15 px-4 py-2.5 text-[13px] text-accent transition-colors hover:bg-accent/25 disabled:opacity-40"
        >
          <Shine color="#ffffff" opacity={0.18} duration={1.5} enableOnHover>
            <span>Ask</span>
          </Shine>
        </RippleButton>
      </div>
    </div>
  );
}
