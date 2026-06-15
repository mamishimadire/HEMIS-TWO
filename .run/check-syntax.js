const fs = require('fs');
const code = fs.readFileSync('.run/rule12.script.js','utf8');
try {
  new Function(code);
  console.log('ok');
} catch (e) {
  console.error('name=' + e.name);
  console.error('message=' + e.message);
  console.error('stack=' + e.stack);
  process.exit(1);
}
