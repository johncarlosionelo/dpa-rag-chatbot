import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';

export function RichText({ value }: { value: string }) {
  return (
    <div className="text-[14px] leading-relaxed text-muted">
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        components={{
          p: ({ children }) => <p className="mb-2.5 last:mb-0">{children}</p>,
          strong: ({ children }) => (
            <strong className="font-semibold text-ink">{children}</strong>
          ),
          em: ({ children }) => <em className="text-ink/80 italic">{children}</em>,
          ul: ({ children }) => (
            <ul className="mb-3 flex list-disc flex-col gap-1.5 pl-6 last:mb-0 marker:text-accent">
              {children}
            </ul>
          ),
          ol: ({ children }) => (
            <ol className="mb-3 flex list-decimal flex-col gap-1.5 pl-6 last:mb-0 marker:text-accent">
              {children}
            </ol>
          ),
          li: ({ children }) => (
            <li className="pl-1 [&>p]:mb-0 [&>ol]:mt-1.5 [&>ul]:mt-1.5">{children}</li>
          ),
          code: ({ children }) => (
            <code className="rounded bg-panel px-1 py-0.5 font-mono text-[12.5px] text-accent">
              {children}
            </code>
          ),
          blockquote: ({ children }) => (
            <blockquote className="mb-2.5 border-l-2 border-accent/50 pl-3 text-ink/75">
              {children}
            </blockquote>
          ),
          a: ({ children, href }) => (
            <a href={href} target="_blank" rel="noreferrer" className="text-accent underline">
              {children}
            </a>
          ),
          hr: () => <hr className="my-3 border-line" />,
          table: ({ children }) => (
            <div className="mb-2.5 overflow-x-auto">
              <table className="w-full border-collapse text-[13px]">{children}</table>
            </div>
          ),
          th: ({ children }) => (
            <th className="border border-line px-2 py-1 text-left font-medium text-ink/90">
              {children}
            </th>
          ),
          td: ({ children }) => (
            <td className="border border-line px-2 py-1 text-muted">{children}</td>
          ),
        }}
      >
        {value}
      </ReactMarkdown>
    </div>
  );
}
