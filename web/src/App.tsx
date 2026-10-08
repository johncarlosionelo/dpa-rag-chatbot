import { useEffect, useRef, useState } from 'react';
import { motion } from 'motion/react';
import { AskBar } from './components/AskBar';
import { Citation } from './components/Citation';
import { ActBadge } from './components/ActBadge';
import { act, ask, SUGGESTIONS, type ChatTurn } from './api';

export function App() {
  const [turns, setTurns] = useState<ChatTurn[]>([]);
  const [act_, setAct] = useState<{ act: string; sections: number } | null>(null);
  const [ready, setReady] = useState(false);
  const [online, setOnline] = useState(true);
  const bottom = useRef<HTMLDivElement>(null);

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

  useEffect(() => {
    bottom.current?.scrollIntoView({ behavior: 'smooth', block: 'end' });
  }, [turns]);

  const send = async (question: string) => {
    const trimmed = question.trim();
    if (trimmed.length === 0) {
      return;
    }

    setTurns((current) => [
      ...current,
      { role: 'user', question: trimmed },
      { role: 'assistant', question: trimmed, pending: true, sources: [] },
    ]);

    try {
      const result = await ask(trimmed);
      setTurns((current) => {
        const next = [...current];
        const last = next[next.length - 1];
        next[next.length - 1] = {
          role: 'assistant',
          question: trimmed,
          answer: result.answer,
          caveat: result.caveat,
          sources: result.sources,
          model: result.model,
          score: result.score,
        };
        return last ? next : current;
      });
    } catch {
      setTurns((current) => {
        const next = [...current];
        next[next.length - 1] = {
          role: 'assistant',
          question: trimmed,
          answer: 'I could not reach the answer service. Check that the API is running and try again.',
          sources: [],
          failed: true,
        };
        return next;
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
        <span
          className={`shrink-0 font-mono text-[10.5px] uppercase tracking-widest ${
            online ? 'text-muted' : 'text-warn'
          }`}
        >
          {online ? 'ready' : 'offline'}
        </span>
      </header>

      <div className="min-h-0 flex-1 overflow-y-auto px-5 py-6">
        <div className="mx-auto flex w-full max-w-3xl flex-col gap-6">
          {turns.length === 0 && <Intro onPick={send} />}

          {turns.map((turn, index) =>
            turn.role === 'user' ? (
              <Question key={`q-${index}`} text={turn.question} />
            ) : (
              <Answer key={`a-${index}`} turn={turn} />
            ),
          )}

          <div ref={bottom} />
        </div>
      </div>

      <AskBar onSend={send} />
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
      className="flex justify-end"
    >
      <p className="max-w-[85%] rounded-xl rounded-br-sm border border-line bg-panel px-3.5 py-2.5 text-[13.5px] leading-relaxed">
        {text}
      </p>
    </motion.div>
  );
}

function Answer({ turn }: { turn: ChatTurn }) {
  if (turn.pending) {
    return (
      <div className="flex items-center gap-2 text-muted">
        <motion.span
          className="size-1.5 rounded-full bg-accent"
          animate={{ opacity: [0.3, 1, 0.3] }}
          transition={{ duration: 1.1, repeat: Infinity, ease: 'easeInOut' }}
        />
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
      <p className="text-[14px] leading-relaxed whitespace-pre-wrap text-ink/95">
        {turn.answer}
      </p>

      {turn.caveat && (
        <p className="rounded-lg border border-warn/40 bg-warn/10 px-3 py-2 text-[12.5px] leading-relaxed text-warn">
          {turn.caveat}
        </p>
      )}

      {turn.sources && turn.sources.length > 0 && (
        <div className="flex flex-col gap-2">
          <span className="font-mono text-[10px] uppercase tracking-widest text-muted">
            sections used
          </span>
          <ul className="flex flex-wrap gap-1.5">
            {turn.sources.map((source) => (
              <Citation key={source.number} source={source} />
            ))}
          </ul>
        </div>
      )}

      {turn.model && (
        <span className="font-mono text-[10px] text-muted/70">
          {turn.model} / relevance {turn.score?.toFixed(2)}
        </span>
      )}
    </motion.div>
  );
}
