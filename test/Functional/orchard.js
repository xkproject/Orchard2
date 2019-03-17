const child_process = require('child_process');
const fs = require('fs');
const rimraf = require('rimraf');

global.log = function (msg) {
    let now = new Date().toLocaleTimeString();
    global._LOG_ += `[${now}] ${msg}\n`;
};

module.exports = {
    printLog: function () {
        console.log(global._LOG_);
    },
    run: function (dir, assembly, clean) {
        global._LOG_ = "";

        if (fs.existsSync(dir + 'bin/release/netcoreapp2.2/' + assembly)) {
            global.log('Application already built, skipping build');
        }
        else {
            global.log('Building ...');
            child_process.spawnSync('dotnet', ['build', '-c', 'release'], { cwd: dir });
        }

        if (clean === true) {
            rimraf(dir + 'App_Data', function () { global.log('App_Data deleted'); });
        }

        global.log('Starting application ...');
        let server = child_process.spawn('dotnet', ['bin/release/netcoreapp2.2/' + assembly], { cwd: dir });

        server.stdout.on('data', (data) => {
            global.log(data);
        });

        server.stderr.on('data', (data) => {
            global.log(`stderr: ${data}`);
        });

        server.on('close', (code) => {
            global.log(`Server process exited with code ${code}`);
        });

        global.__SERVER_GLOBAL__ = server;

        return "http://localhost:5000";
    },
    stop: function () {
        let server = global.__SERVER_GLOBAL__;

        if (server) {
            server.kill('SIGINT');
        }
    },
    cleanAppData: function (dir) {
        rimraf(dir + 'App_Data', function () { global.log('App_Data deleted'); });
    }
};