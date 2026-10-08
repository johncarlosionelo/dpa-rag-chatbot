export type SourceRef = {
  number: string;
  title: string;
  text: string;
};

export type ChatResponse = {
  answer: string;
  caveat: string | null;
  sources: SourceRef[];
  model: string | null;
  score: number;
  kind: string;
  scored: boolean;
  suggestions: SourceRef[];
};

export type ChatTurn = {
  id?: number;
  role: 'user' | 'assistant';
  question: string;
  answer?: string;
  sources?: SourceRef[];
  caveat?: string | null;
  model?: string | null;
  score?: number;
  kind?: string;
  scored?: boolean;
  suggestions?: SourceRef[];
  pending?: boolean;
  failed?: boolean;
};

export type MemoryTurn = {
  role: 'user' | 'assistant';
  content: string;
};

export function toMemory(turns: ChatTurn[], limit = 4): MemoryTurn[] {
  return turns
    .filter((turn) => !turn.pending && !turn.failed && turn.answer)
    .slice(-limit)
    .flatMap((turn) => [
      { role: 'user' as const, content: turn.question },
      { role: 'assistant' as const, content: turn.answer ?? '' },
    ]);
}

export async function ask(question: string, history: MemoryTurn[] = []): Promise<ChatResponse> {
  const response = await fetch('/api/chat', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ question, history }),
  });

  return (await response.json()) as ChatResponse;
}

export async function act(): Promise<{ act: string; sections: number; backend: string }> {
  const response = await fetch('/api/act');
  if (!response.ok) {
    throw new Error(`request failed with ${response.status}`);
  }

  return (await response.json()) as { act: string; sections: number; backend: string };
}

export const SUGGESTIONS = [
  'What are the penalties for unauthorised access?',
  'Can a data subject ask for information in a portable format?',
  'What is sensitive personal information?',
  'How long must a company keep personal information?',
  'Who is exempt from the Act?',
  'What must a company do when personal information is breached?',
];
