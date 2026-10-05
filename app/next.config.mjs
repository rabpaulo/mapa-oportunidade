/** @type {import('next').NextConfig} */
const config = {
  reactStrictMode: true,
  // O navegador acessa apenas Next; a API permanece no servidor local 8000.
  async rewrites() {
    return [{ source: '/api/:path*', destination: 'http://127.0.0.1:8000/api/:path*' }];
  },
};
export default config;
