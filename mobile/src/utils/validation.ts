import { z } from 'zod';

const guid = z.string().regex(
  /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/,
  'یک مورد را انتخاب کنید.',
);

export const loginSchema = z.object({
  username: z.string().trim().min(1, 'نام کاربری را وارد کنید.'),
  password: z.string().min(1, 'رمز عبور را وارد کنید.'),
});

export type LoginValues = z.infer<typeof loginSchema>;

export const filingSchema = z.object({
  title: z.string().trim().min(1, 'عنوان سند را وارد کنید.').max(500, 'عنوان حداکثر ۵۰۰ نویسه است.'),
  description: z.string().max(4000, 'توضیح حداکثر ۴۰۰۰ نویسه است.'),
  categoryId: guid,
  documentTypeId: guid,
});

export type FilingValues = z.infer<typeof filingSchema>;

export const passwordSchema = z
  .object({
    currentPassword: z.string().min(1, 'رمز فعلی را وارد کنید.'),
    newPassword: z.string().min(12, 'رمز جدید باید حداقل ۱۲ نویسه باشد.'),
    confirmPassword: z.string().min(1, 'تکرار رمز را وارد کنید.'),
  })
  .refine((value) => value.newPassword === value.confirmPassword, {
    path: ['confirmPassword'],
    message: 'تکرار رمز با رمز جدید یکسان نیست.',
  });

export type PasswordValues = z.infer<typeof passwordSchema>;
