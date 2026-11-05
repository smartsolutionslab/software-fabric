import { Button } from '@/components/ui/button';

interface FailureProps {
  message: string;
  retry: string;
  onRetry: () => unknown;
}

/** A read that failed, said so, with a way to try it again. */
export function Failure({ message, retry, onRetry }: FailureProps) {
  return (
    <div role="alert" className="flex items-center gap-2 text-sm">
      <span>{message}</span>
      <Button type="button" variant="outline" size="sm" aria-label={retry} onClick={() => void onRetry()}>
        Try again
      </Button>
    </div>
  );
}
