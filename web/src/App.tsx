import { useEffect, useRef, useState } from 'react';
import { motion } from 'motion/react';
import { AskBar } from './components/AskBar';
import { ScrollArea } from './components/anim/scroll';
import { Sources } from './components/Citation';
import { RichText } from './components/RichText';
import { CopyButton } from './components/CopyButton';
import { SparklesIcon } from './components/anim/SparklesIcon';
import { ActBadge } from './components/ActBadge';
import { act, ask, toMemory, SUGGESTIONS, type ChatTurn } from './api';

export function App() {
  const [turns, setTurns] = useState<ChatTurn[]>([]);
  const [act_, setAct] = useState<{ act: string; sections: number; backend: string } | null>(null);
  const [ready, setReady] = useState(false);
  const [, setOnline] = useState(true);
  const pending = turns.some((turn) => turn.pending);
  const bottom = useRef<HTMLDivElement>(null);
  const scroller = useRef<HTMLDivElement>(null);

  useEffect(() => {
    act()
      .then((value) => {
        setAct(value);
        setReady(true);
      })
      .catch(() => {
        setOnline(false);
        setReady(true);
      });
  }, []);

  const send = async (question: string) => {
    const trimmed = question.trim();
    if (trimmed.length === 0) {
      return;
    }

    const id = Date.now() + Math.random();
    const history = toMemory(turns);

    setTurns((current) => [
      ...current,
      { id, role: 'user', question: trimmed },
      { id, role: 'assistant', question: trimmed, pending: true, sources: [] },
    ]);

    const settle = (patch: Partial<ChatTurn>) => {
      setTurns((current) => current.map((turn) => (turn.id === id && turn.role === 'assistant' ? { ...turn, ...patch } : turn)));
    };

    try {
      const result = await ask(trimmed, history);
      settle({
        answer: result.answer,
        caveat: result.caveat,
        sources: result.sources,
        model: result.model,
        score: result.score,
        kind: result.kind,
        scored: result.scored,
        suggestions: result.suggestions,
        pending: false,
      });
    } catch {
      settle({
        answer: 'I could not reach the answer service. Check that the API is running and try again.',
        sources: [],
        failed: true,
        pending: false,
      });
    }
  };

  return (
    <div className="flex h-full flex-col">
      <header className="flex shrink-0 items-center justify-between gap-4 border-b border-line px-5 py-3.5">
        <div className="flex min-w-0 items-center gap-2.5">
          <h1 className="truncate text-[15px] font-semibold tracking-tight">
            Data Privacy Act of 2012
          </h1>
          <ActBadge act={act_} ready={ready} />
        </div>
        <a
          href="https://humain.ph/"
          target="_blank"
          rel="noreferrer"
          className="group flex shrink-0 items-center gap-1.5 font-mono text-[10.5px] uppercase tracking-widest text-muted transition-colors hover:text-ink"
        >
          <SparklesIcon size={12} className="text-accent" loop animation="default" />
          HumAIn
        </a>
      </header>

      <ScrollArea className="min-h-0 flex-1 pr-2" stickToBottom viewportRef={scroller}>
        <div className="px-3 py-6">
          <div className="mx-auto flex w-full max-w-3xl flex-col gap-6">
          {turns.length === 0 && <Intro onPick={send} />}

          {turns.map((turn, index) =>
            turn.role === 'user' ? (
              <Question key={`q-${turn.id ?? index}`} text={turn.question} />
            ) : (
              <Answer key={`a-${turn.id ?? index}`} turn={turn} />
            ),
          )}

            <div ref={bottom} />
          </div>
        </div>
      </ScrollArea>

      <AskBar onSend={send} busy={pending} />
    </div>
  );
}

function Intro({ onPick }: { onPick: (question: string) => void }) {
  return (
    <motion.section
      initial={{ opacity: 0, y: 10 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.32, ease: [0.22, 1, 0.36, 1] }}
      className="flex flex-col gap-6"
    >
      <div>
        <h2 className="text-[22px] leading-tight font-semibold tracking-tight">
          Ask the Act, get the section.
        </h2>
        <p className="mt-2 max-w-xl text-[13.5px] leading-relaxed text-muted">
          Every answer is retrieved from the text of Republic Act No. 10379 and cites the
          sections it used. If the Act does not cover your question, this will say so instead
          of guessing.
        </p>
      </div>

      <ul className="flex flex-col gap-2">
        {SUGGESTIONS.map((text, index) => (
          <motion.li
            key={text}
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: 0.06 + index * 0.04, duration: 0.28 }}
          >
            <button
              type="button"
              onClick={() => onPick(text)}
              className="w-full rounded-lg border border-line bg-panel/50 px-3.5 py-2.5 text-left text-[13px] text-ink/90 transition-colors hover:border-accent/50 hover:bg-panel"
            >
              {text}
            </button>
          </motion.li>
        ))}
      </ul>
    </motion.section>
  );
}

function Question({ text }: { text: string }) {
  return (
    <motion.div
      initial={{ opacity: 0, y: 6 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.22 }}
      className="group flex items-start justify-end gap-2"
    >
      <div className="max-w-[85%] rounded-xl rounded-br-sm border border-line bg-panel px-3.5 py-2.5 text-[13.5px] leading-relaxed">
        {text}
      </div>
      <CopyButton value={text} className="mt-0.5 opacity-0 transition-opacity focus-visible:opacity-100 group-hover:opacity-100" />
    </motion.div>
  );
}

function Answer({ turn }: { turn: ChatTurn }) {
  const answer = turn.answer ?? '';

  if (turn.pending) {
    return (
      <div className="flex items-center gap-2 text-muted">
        <SparklesIcon size={13} className="text-accent" loop animation="default" />
        <span className="font-mono text-[11px] uppercase tracking-widest">
          searching the Act
        </span>
      </div>
    );
  }

  return (
    <motion.div
      initial={{ opacity: 0, y: 6 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.26, ease: [0.22, 1, 0.36, 1] }}
      className="flex flex-col gap-3"
    >
      <div className="group flex items-start gap-2">
        <div className="min-w-0 flex-1">
          <RichText value={answer} />
        </div>
        <CopyButton value={answer} label="Copy answer" className="mt-0.5 opacity-0 transition-opacity focus-visible:opacity-100 group-hover:opacity-100" />
      </div>

      {turn.caveat && (
        <p className="rounded-lg border border-warn/40 bg-warn/10 px-3 py-2 text-[12.5px] leading-relaxed text-warn">
          {turn.caveat}
        </p>
      )}

      {turn.sources && turn.sources.length > 0 && (
        <Sources label="sections used" sources={turn.sources} />
      )}

      {turn.suggestions && turn.suggestions.length > 0 && (
        <Sources label="sections the Act does cover" sources={turn.suggestions} />
      )}

      {turn.model && turn.scored && (
        <span className="font-mono text-[10px] text-muted/70">
          {turn.model} / relevance {Math.min(turn.score ?? 0, 1).toFixed(2)}
        </span>
      )}
    </motion.div>
  );
}
