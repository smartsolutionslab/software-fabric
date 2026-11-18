import { h } from 'vue';
import { toast } from 'vue-sonner';

/**
 * Reports what a mutation did, in the toasts the app shell shows (<Toaster />): a success as a status message, which a
 * screen reader reads when it is idle, and a failure as an alert, which it reads at once.
 */
export const notify = {
  success(message: string) {
    toast.success(() => h('span', { role: 'status' }, message));
  },
  failure(message: string) {
    toast.error(() => h('span', { role: 'alert' }, message));
  },
};
