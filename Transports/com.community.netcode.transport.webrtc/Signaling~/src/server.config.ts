export const serverConfig = {
  port: +(process.env.PORT || 4000),
  auth: {
    token: process.env.AUTH_TOKEN || '',
  },
};
