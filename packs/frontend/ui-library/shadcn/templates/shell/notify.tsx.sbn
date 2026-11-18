import { toast } from 'sonner';

/**
 * Reports what a mutation did, in the toasts the app shell shows (<Toaster />): a success as a status message, which a
 * screen reader reads when it is idle, and a failure as an alert, which it reads at once.
 */
export const notify = {
  success(message: string) {
    toast.success(<span role="status">{message}</span>);
  },
  failure(message: string) {
    toast.error(<span role="alert">{message}</span>);
  },
};
