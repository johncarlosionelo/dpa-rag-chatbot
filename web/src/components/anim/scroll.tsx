'use client';

import * as React from 'react';
import { motion, useMotionValue, useSpring, useTransform } from 'motion/react';

import { cn } from '@/lib/utils';

type ScrollAreaProps = {
  children: React.ReactNode;
  className?: string;
  viewportClassName?: string;
  fadeMs?: number;
  stickToBottom?: boolean;
  viewportRef?: React.Ref<HTMLDivElement>;
};

export function ScrollArea({
  children,
  className,
  viewportClassName,
  fadeMs = 900,
  stickToBottom = false,
  viewportRef,
}: ScrollAreaProps) {
  const viewport = React.useRef<HTMLDivElement>(null);
  const hide = React.useRef<ReturnType<typeof setTimeout> | null>(null);
  const wasPinned = React.useRef(true);
  const lastContent = React.useRef(0);
  const [active, setActive] = React.useState(false);
  const [overflowing, setOverflowing] = React.useState(false);

  const offset = useMotionValue(0);
  const extent = useMotionValue(1);
  const smoothOffset = useSpring(offset, { stiffness: 320, damping: 34, mass: 0.5 });
  const smoothExtent = useSpring(extent, { stiffness: 320, damping: 34, mass: 0.5 });

  const thumbSize = useTransform(smoothExtent, (value) => `${Math.max(8, value * 100)}%`);
  const thumbShift = useTransform(smoothOffset, (value) => `${value * 100}%`);

  const paint = React.useCallback((bump = true) => {
    const el = viewport.current;
    if (!el) return;

    const content = el.scrollHeight;
    const track = el.clientHeight;
    const top = el.scrollTop;

    if (content <= track + 1) {
      setOverflowing(false);
      wasPinned.current = true;
      return;
    }

    setOverflowing(true);

    const ratio = track / content;
    const progress = top / Math.max(1, content - track);

    extent.set(ratio);
    offset.set(ratio >= 1 ? 0 : progress * (1 - ratio));

    if (!bump) return;

    setActive(true);

    if (hide.current) clearTimeout(hide.current);
    hide.current = setTimeout(() => setActive(false), fadeMs);
  }, [extent, offset]);

  const onScroll = React.useCallback(() => {
    const el = viewport.current;
    if (!el) return;

    const gap = el.scrollHeight - (el.scrollTop + el.clientHeight);
    wasPinned.current = gap <= 24;
    paint();
  }, [paint]);

  const measure = React.useCallback(() => {
    const el = viewport.current;
    if (!el) return;

    const content = el.scrollHeight;
    const grew = content > lastContent.current + 1;
    lastContent.current = content;

    if (grew && stickToBottom && wasPinned.current) {
      el.scrollTop = content;
    }

    paint(false);
  }, [paint, stickToBottom]);

  React.useEffect(() => {
    const el = viewport.current;
    if (!el) return;

    lastContent.current = el.scrollHeight;

    const observer = new ResizeObserver(() => measure());
    observer.observe(el);

    for (const child of Array.from(el.children)) {
      observer.observe(child);
    }

    return () => observer.disconnect();
  }, [measure, children]);

  React.useEffect(() => () => {
    if (hide.current) clearTimeout(hide.current);
  }, []);

  return (
    <div className={cn('relative', className)}>
      <div
        ref={mergeRefs(viewport, viewportRef)}
        onScroll={onScroll}
        style={{ scrollbarWidth: 'none' }}
        className={cn('h-full overflow-y-auto overscroll-contain', viewportClassName)}
      >
        {children}
      </div>

      {overflowing && (
        <div aria-hidden className="pointer-events-none absolute inset-y-2 right-0.5 w-1.5">
          <motion.div
            className="absolute inset-x-0 rounded-full bg-foreground/30"
            style={{ height: thumbSize, top: thumbShift }}
            animate={{ opacity: active ? 1 : 0 }}
            transition={{ duration: 0.24, ease: 'easeOut' }}
          />
        </div>
      )}
    </div>
  );
}

function mergeRefs<T>(...refs: Array<React.Ref<T> | undefined>) {
  return (value: T) => {
    for (const ref of refs) {
      if (typeof ref === 'function') ref(value);
      else if (ref && typeof ref === 'object') {
        (ref as React.MutableRefObject<T | null>).current = value;
      }
    }
  };
}
