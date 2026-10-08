import { TerminalIcon } from './anim/TerminalIcon';
import { LightbulbIcon } from './anim/LightbulbIcon';

export function ActBadge({
  act,
  ready,
}: {
  act: { act: string; sections: number } | null;
  ready: boolean;
}) {
  if (!ready || !act) {
    return (
      <span className="flex items-center gap-1.5 rounded-md border border-line px-1.5 py-0.5 font-mono text-[10px] text-muted">
        <TerminalIcon size={11} />
        loading
      </span>
    );
  }

  return (
    <span className="flex items-center gap-1.5 rounded-md border border-line px-1.5 py-0.5 font-mono text-[10px] text-muted">
      <LightbulbIcon size={11} />
      {act.sections} sections
    </span>
  );
}
