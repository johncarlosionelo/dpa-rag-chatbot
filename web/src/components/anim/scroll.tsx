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
  streaming?: boolean;
  viewportRef?: React.Ref<HTMLDivElement>;
};

export function ScrollArea({
  children,
  className,
  viewportClassName,
  fadeMs = 900,
  stickToBottom = false,
  viewportRef,
  streaming: streamingProp = false,
}: ScrollAreaProps) {
  const viewport = React.useRef<HTMLDivElement>(null);
  const hide = React.useRef<ReturnType<typeof setTimeout> | null>(null);
  const wasPinned = React.useRef(true);
  const lastContent = React.useRef(0);
  const lastHeight = React.useRef(0);
  const frame = React.useRef<number | null>(null);
  const animating = React.useRef(false);
  const settle = React.useRef<ReturnType<typeof setTimeout> | null>(null);
  const streaming = React.useRef(streamingProp);

  React.useEffect(() => {
    streaming.current = streamingProp;
  }, [streamingProp]);
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

  const release = React.useCallback(() => {
    animating.current = false;

    const el = viewport.current;
    if (el) {
      const gap = el.scrollHeight - (el.scrollTop + el.clientHeight);
      wasPinned.current = gap <= 24;
    }
  }, []);

  const onScroll = React.useCallback(() => {
    const el = viewport.current;
    if (!el || animating.current) return;

    const gap = el.scrollHeight - (el.scrollTop + el.clientHeight);
    wasPinned.current = gap <= 24;
    paint();
  }, [paint]);

  const releaseRef = React.useRef(release);

  React.useEffect(() => {
    releaseRef.current = release;
  }, [release]);

  const follow = React.useCallback((behavior: ScrollBehavior) => {
    const el = viewport.current;
    if (!el) return;

    if (frame.current) return;

    frame.current = requestAnimationFrame(() => {
      frame.current = null;

      const current = viewport.current;
      if (!current || !stickToBottom || !wasPinned.current) return;

      const gap = current.scrollHeight - (current.scrollTop + current.clientHeight);

      if (gap <= 1) return;

      animating.current = true;
      current.scrollTo({ top: current.scrollHeight, behavior });

      if (behavior === 'smooth') {
        current.addEventListener('scroll', releaseRef.current, { once: true });
      } else {
        animating.current = false;
      }
    });
  }, [stickToBottom]);

  const measure = React.useCallback(() => {
    const el = viewport.current;
    if (!el) return;

    const content = el.scrollHeight;
    const grew = content > lastContent.current + 1;
    const shrank = el.clientHeight < lastHeight.current - 1;

    lastContent.current = content;
    lastHeight.current = el.clientHeight;

    if (!stickToBottom || !wasPinned.current) {
      paint(false);
      return;
    }

    if (shrank) {
      follow('auto');
    } else if (grew) {
      follow(streaming.current ? 'auto' : 'smooth');
    }

    paint(false);
  }, [follow, paint, stickToBottom]);

  React.useEffect(() => {
    const el = viewport.current;
    if (!el) return;

    const observer = new ResizeObserver(() => measure());
    observer.observe(el);

    const mutations = new MutationObserver(() => measure());

    for (const child of Array.from(el.children)) {
      observer.observe(child);
      mutations.observe(child, { childList: true, subtree: true, characterData: true });
    }

    return () => {
      observer.disconnect();
      mutations.disconnect();
    };
  }, [measure]);

  React.useEffect(() => () => {
    if (hide.current) clearTimeout(hide.current);
    if (frame.current) cancelAnimationFrame(frame.current);
    if (settle.current) clearTimeout(settle.current);
  }, []);

  React.useEffect(() => {
    if (settle.current) clearTimeout(settle.current);

    settle.current = setTimeout(() => {
      if (!animating.current) release();
    }, 140);
  }, [release, streamingProp]);

  return (
    <div className={cn('relative', className)}>
      <div
        ref={mergeRefs(viewport, viewportRef)}
        onScroll={onScroll}
        style={{ scrollbarWidth: 'none', scrollBehavior: 'auto' }}
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
