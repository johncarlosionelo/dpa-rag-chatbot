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
};

export type ChatTurn = {
  role: 'user' | 'assistant';
  question: string;
  answer?: string;
  sources?: SourceRef[];
  caveat?: string | null;
  model?: string | null;
  score?: number;
  pending?: boolean;
  failed?: boolean;
};

export async function ask(question: string): Promise<ChatResponse> {
  const response = await fetch('/api/chat', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ question }),
  });

  if (!response.ok) {
    throw new Error(`request failed with ${response.status}`);
  }

  return (await response.json()) as ChatResponse;
}

export async function act(): Promise<{ act: string; sections: number }> {
  const response = await fetch('/api/act');
  if (!response.ok) {
    throw new Error(`request failed with ${response.status}`);
  }

  return (await response.json()) as { act: string; sections: number };
}

export const SUGGESTIONS = [
  'What are the penalties for unauthorised access?',
  'Can a data subject ask for information in a portable format?',
  'What is sensitive personal information?',
  'How long must a company keep personal information?',
  'Who is exempt from the Act?',
  'How do I bake sourdough bread?',
];
